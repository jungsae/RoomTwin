package com.roomtwin.nativeai

import android.os.PowerManager
import java.util.Locale

internal object BenchmarkDashboard {
    fun summary(session: BenchSession): String = buildString {
        val header = session.header
        appendLine("${header.request.workload.title} · ${header.request.order.title}")
        appendLine("${header.createdUtc} · ${header.device} · ${header.os}")
        appendLine("Session ${header.id}")
        appendLine("Git ${header.gitSha} · App ${header.appVersion} · LiteRT-LM ${header.liteRt}")
        appendLine("${header.model.name} · ${value(header.model.bytes, " B")}")
        appendLine("${endLabel(session.end)} · 실행 ${session.trials.size}/${header.request.plannedTrials}")
        val success = session.trials.count { it.outcome == BenchOutcome.SUCCESS }
        val errors = session.trials.count { it.outcome == BenchOutcome.ERROR }
        val interruptions = session.trials.count { it.outcome == BenchOutcome.INTERRUPTED }
        appendLine("완료율 ${ratio(success, session.trials.size)} · 오류 $errors · 실행 중단 $interruptions")
        val scored = session.trials.mapNotNull { it.evaluationCorrect }
        if (scored.isNotEmpty()) appendLine("채점된 시도의 기준 답변 일치 ${ratio(scored.count { it }, scored.size)}")
        appendLine()
        appendLine("조건별 비교")
        session.trials.groupBy { it.condition }.forEach { (condition, trials) ->
            appendLine(conditionLabel(condition))
            for (phase in BenchPhase.entries) {
                val group = trials.filter { it.phase == phase }
                if (group.isEmpty()) continue
                val successful = group.filter { it.outcome == BenchOutcome.SUCCESS }
                val stats = BenchStats.of(successful.map { it.latencyMillis })
                val tokens = successful.mapNotNull { it.outputTokens }
                val decode = successful.flatMap { trial -> trial.turns.mapNotNull { it.native.decodeTokensPerSecond } }
                appendLine("${phaseLabel(phase)} · 완료 ${ratio(successful.size, group.size)} · 오류 ${group.count { it.outcome == BenchOutcome.ERROR }} · 중단 ${group.count { it.outcome == BenchOutcome.INTERRUPTED }}")
                if (stats != null) {
                    appendLine("  평균 ${number(stats.mean)} · 중앙값 ${number(stats.median)} · P95 ${stats.p95} · 표준편차 ${number(stats.standardDeviation)} ms")
                    appendLine("  최소–최대 ${stats.minimum}–${stats.maximum} ms · 출력 평균 ${average(tokens.map(Int::toDouble))} tokens (${tokens.size}/${successful.size})")
                    appendLine("  턴별 decode 평균 ${average(decode)} tok/s · 열 ${thermal(group.first().before.thermalStatus)} → ${thermal(group.last().after.thermalStatus)}")
                }
                val scoredGroup = group.mapNotNull { it.evaluationCorrect }
                if (scoredGroup.isNotEmpty()) {
                    appendLine("  기준 답변 일치 ${ratio(scoredGroup.count { it }, scoredGroup.size)}")
                }
            }
        }
        append("표본 수, 실행 순서, 발열과 실제 토큰 길이를 함께 확인하세요. 기술 통계만 제공하며 백엔드 우위를 판정하지 않습니다.")
    }

    fun trial(trial: BenchTrial): String = buildString {
        appendLine("#${trial.index} · ${conditionLabel(trial.condition)} · ${phaseLabel(trial.phase)}")
        appendLine("${trial.outcome}${trial.errorCode?.let { " · $it" }.orEmpty()} · ${trial.latencyMillis} ms · 출력 ${value(trial.outputTokens, " tokens")}")
        trial.evaluationCorrect?.let { appendLine(if (it) "기준 답변과 일치" else "기준 답변과 불일치") }
        appendLine("시작: ${resources(trial.before)}")
        appendLine("종료: ${resources(trial.after)}")
        appendLine("샘플 최대 PSS ${value(trial.peakSampledPssKiB, " KiB")} · RSS ${value(trial.peakSampledRssKiB, " KiB")} · 열 ${thermal(trial.peakThermalStatus)}")
        appendLine("GPU 전용 메모리 N/A")
        for (turn in trial.turns) {
            val native = turn.native
            appendLine()
            appendLine("턴 ${turn.turn} · 입력 ${turn.inputCharacters}자 · 이미지 ${turn.imageCount}장")
            appendLine("요청 ${turn.latencyMillis} ms · 모델 초기화 ${value(native.initializationMillis, " ms")}")
            appendLine("첫 표시 청크 ${value(turn.firstChunkMillis, " ms")} · 실제 TTFT N/A")
            appendLine("LiteRT TTFT 추정 ${value(native.runtimeTtftEstimateMillis, " ms")}")
            appendLine("Prefill ${value(native.prefillMillis, " ms")} · Decode ${value(native.decodeMillis, " ms")}")
            appendLine("실제 입력 ${value(native.inputTokens, " tokens")} · 출력 ${value(native.outputTokens, " tokens")}")
            appendLine("Prefill ${value(native.prefillTokensPerSecond, " tok/s")} · Decode ${value(native.decodeTokensPerSecond, " tok/s")}")
            appendLine("KV 컨텍스트 ${value(native.contextTokens)} / ${value(native.contextCapacity)} tokens")
        }
    }.trimEnd()

    fun progress(progress: BenchProgress): String {
        val title = "${progress.index}/${progress.planned} · ${conditionLabel(progress.condition)} · ${phaseLabel(progress.phase)}"
        return progress.completed?.let { "$title\n${trial(it)}" }
            ?: "$title\n${resources(progress.sample ?: progress.before)}"
    }

    fun timeline(trials: List<BenchTrial>): String = trials.joinToString("\n") { trial ->
        val decode = trial.turns.mapNotNull { it.native.decodeTokensPerSecond }
        "#${trial.index} ${trial.condition.backend} · ${phaseLabel(trial.phase)} · ${trial.latencyMillis} ms · " +
            "PSS ${value(trial.peakSampledPssKiB, " KiB")} / RSS ${value(trial.peakSampledRssKiB, " KiB")} · " +
            "${thermal(trial.before.thermalStatus)} → ${thermal(trial.after.thermalStatus)} · decode ${average(decode)} tok/s"
    }

    fun phaseLabel(phase: BenchPhase): String = when (phase) {
        BenchPhase.COLD -> "Cold"
        BenchPhase.WARM_UP -> "Warm-up"
        BenchPhase.WARM -> "Warm"
    }

    fun endLabel(end: BenchEnd): String = when (end) {
        BenchEnd.FINISHED -> "실행 종료"
        BenchEnd.STOP_REQUESTED -> "사용자 또는 화면 종료로 중단"
        BenchEnd.THERMAL_LIMIT -> "심각한 발열로 중단"
        BenchEnd.OUT_OF_MEMORY -> "메모리 부족으로 중단"
        BenchEnd.INPUT_PREPARATION_FAILED -> "입력 이미지 준비 실패"
        BenchEnd.MODEL_CHANGED -> "모델 파일 변경으로 중단"
        BenchEnd.INCOMPLETE -> "종료 기록 없음"
    }

    private fun conditionLabel(condition: BenchCondition): String {
        val thinking = if (condition.thinking) "ON" else "OFF"
        val method = when (condition.method) {
            BenchMethod.DIRECT -> " · 두 이미지 직접 입력"
            BenchMethod.SUMMARIES -> " · 개별 요약 비교"
            BenchMethod.STANDARD -> ""
        }
        val load = if (condition.loadPercent > 0) " · 입력 부하 추정 ${condition.loadPercent}%" else ""
        return "${condition.backend} · Context ${condition.contextLength} · Thinking $thinking$method$load"
    }

    private fun resources(data: BenchResources) =
        "PSS ${value(data.pssKiB, " KiB")} · RSS ${value(data.rssKiB, " KiB")} · " +
            "Native heap ${value(data.nativeHeapBytes, " B")} · Java heap ${value(data.javaHeapBytes, " B")}\n" +
            "시스템 여유 ${value(data.availableMemoryBytes, " B")} · 열 ${thermal(data.thermalStatus)} · " +
            "배터리 ${value(data.batteryCelsius, " °C")} / ${value(data.batteryPercent, "%")}"

    private fun thermal(status: Int?): String = when (status) {
        PowerManager.THERMAL_STATUS_NONE -> "NONE"
        PowerManager.THERMAL_STATUS_LIGHT -> "LIGHT"
        PowerManager.THERMAL_STATUS_MODERATE -> "MODERATE"
        PowerManager.THERMAL_STATUS_SEVERE -> "SEVERE"
        PowerManager.THERMAL_STATUS_CRITICAL -> "CRITICAL"
        PowerManager.THERMAL_STATUS_EMERGENCY -> "EMERGENCY"
        PowerManager.THERMAL_STATUS_SHUTDOWN -> "SHUTDOWN"
        else -> "N/A"
    }

    private fun number(value: Double) = String.format(Locale.ROOT, "%.1f", value)

    private fun value(value: Number?, unit: String = ""): String = when (value) {
        null -> "N/A"
        is Double -> number(value) + unit
        else -> value.toString() + unit
    }

    private fun average(values: List<Double>) = if (values.isEmpty()) "N/A" else number(values.average())

    private fun ratio(numerator: Int, denominator: Int): String =
        if (denominator == 0) "N/A" else "$numerator/$denominator (${number(100.0 * numerator / denominator)}%)"
}
