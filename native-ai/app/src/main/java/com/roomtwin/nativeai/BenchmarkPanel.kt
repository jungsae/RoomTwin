package com.roomtwin.nativeai

import android.app.Activity
import android.graphics.Bitmap
import android.graphics.Typeface
import android.text.InputFilter
import android.text.InputType
import android.view.View
import android.view.ViewGroup
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import kotlin.math.roundToInt

internal class BenchmarkPanel(
    private val activity: Activity,
    root: FrameLayout,
    onPhoto: (Int, LabPhotoAction) -> Unit,
    onRun: () -> Unit,
    onStop: () -> Unit,
    onSession: (String) -> Unit,
    onExport: (String, Boolean) -> Unit,
    onDelete: (String?) -> Unit,
) {
    private val form = column().apply { setPadding(dp(16), dp(12), dp(16), dp(24)) }
    private val status = text("").apply { accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE }
    private val run = button("실험 실행", onRun)
    private val stop = button("중단 요청", onStop)
    private val bar = ProgressBar(activity, null, android.R.attr.progressBarStyleHorizontal)
    private val monitor = text("")
    private val settingsSummary = text("")
    private val workload = labeledChoice(form, "워크로드 / 평가", BenchWorkload.entries.map { it.title })
    private val order = labeledChoice(form, "백엔드 / 실행 순서", BenchOrder.entries.map { it.title })
    private val needleGroup = column()
    private val needlePosition = labeledChoice(needleGroup, "정보 삽입 위치", NeedlePosition.entries.map { it.label }, NeedlePosition.MIDDLE.ordinal)
    private val visionGroup = column()
    private val visionCase = labeledChoice(visionGroup, "시각 평가 사례", VisionCase.entries.map { it.title })
    private val imageGroup = column()
    private val fixture = check("내장 평가 이미지 사용", true)
    private val fixtureInfo = text("")
    private val userImages = column()
    private val truthGroup = column()
    private val question = field(truthGroup, "사진에 대한 질문", "", 1000)
    private val expected = field(truthGroup, "기준 답변 (짧은 단일 값)", "", 240)
    private val previews = Array(2) { ImageView(activity) }
    private val slots = Array(2) { slot ->
        column().apply {
            addView(text(if (slot == 0) "첫 번째 이미지 / Before" else "두 번째 이미지 / After"))
            addView(previews[slot].apply {
                contentDescription = if (slot == 0) "첫 번째 이미지 미리보기" else "두 번째 이미지 미리보기"
                scaleType = ImageView.ScaleType.FIT_CENTER
                visibility = View.GONE
            }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(120)))
            addView(row(
                button("카메라") { onPhoto(slot, LabPhotoAction.CAMERA) },
                button("갤러리") { onPhoto(slot, LabPhotoAction.GALLERY) },
                button("제거") { onPhoto(slot, LabPhotoAction.REMOVE) },
            ))
        }
    }
    private val warmups: EditText
    private val repeats: EditText
    private val contextSweep = check("Context Length별 비교", false)
    private val thinkingPair = check("Thinking OFF / ON 비교", false)
    private val history = Spinner(activity)
    private val historyActions = row(
        button("JSONL") { selection()?.let { onExport(it, false) } },
        button("CSV") { selection()?.let { onExport(it, true) } },
        button("선택 삭제") { selection()?.let(onDelete) },
    )
    private val clearAll = button("전체 기록 삭제") { onDelete(null) }
    private val dashboard = text("")
    private val trialGroup = column()
    private val trialChoice = Spinner(activity)
    private val trialDetails = text("")
    private val timeline = text("")
    private val controls = mutableListOf<View>()
    private var sessions: List<BenchSessionHeader> = emptyList()
    private var displayed: BenchSession? = null
    private var settings = PlaygroundSettings()
    private var editing = true
    private var running = false
    private var stopPending = false
    private var latestIndex = 0
    private var completedIndex = 0

    val photoCount get() = if (workload() == BenchWorkload.VISION) visionCase().photos else workload().imageCount

    init {
        root.addView(ScrollView(activity).apply {
            isFillViewport = true
            addView(form)
        })
        form.addView(text("실험 구성", title = true), 0)
        form.addView(text("컨텍스트 실험은 현재 대화의 정보 활용을 평가합니다. 부하 비율은 문자 수 기반 추정이며 실제 토큰 수는 실행 결과에 따로 표시됩니다."))
        form.addView(needleGroup)
        form.addView(visionGroup)
        form.addView(imageGroup.apply {
            addView(fixture)
            addView(fixtureInfo)
            addView(userImages.apply { slots.forEach { addView(it) } })
            addView(truthGroup.apply {
                addView(text("사진에서 확인할 수 있는 기준 답변을 입력하세요. 정규화한 단일 값의 일치 여부를 채점합니다."))
            })
        })
        val options = row()
        form.addView(options)
        fun count(label: String, initial: String, length: Int): EditText {
            val field = column()
            options.addView(field, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            return field(field, label, initial, length).apply { inputType = InputType.TYPE_CLASS_NUMBER }
        }
        warmups = count("Warm-up (0–3)", "1", 1)
        repeats = count("반복 (1–10)", "3", 2)
        form.addView(contextSweep)
        form.addView(thinkingPair)
        val generation = button("생성 설정") {
            showPlaygroundSettings(activity, settings, showBackend = false) {
                settings = it
                updateSettings()
            }
        }
        form.addView(generation)
        form.addView(settingsSummary)
        form.addView(row(run, stop))
        controls.addAll(listOf(workload, order, needleGroup, visionGroup, imageGroup, options, contextSweep, thinkingPair, generation, run))
        form.addView(status)
        form.addView(bar.apply { visibility = View.GONE })
        form.addView(text("Inference Monitor", title = true))
        form.addView(text("첫 표시 청크는 첫 비어 있지 않은 텍스트 콜백입니다. 실제 TTFT와 GPU 전용 메모리는 N/A입니다. LiteRT에 네이티브 취소 API가 있습니다. 이 화면에서 중단을 요청하면 현재 턴을 마친 뒤 다음 턴과 반복을 건너뜁니다."))
        form.addView(monitor)
        form.addView(text("Benchmark Dashboard", title = true))
        form.addView(history)
        form.addView(historyActions)
        form.addView(clearAll)
        form.addView(dashboard)
        form.addView(trialGroup.apply {
            addView(text("반복별 메모리 · 발열 · 생성 속도", title = true))
            addView(timeline)
            addView(trialChoice)
            addView(trialDetails)
        })
        controls.addAll(listOf(history, historyActions, clearAll, trialChoice))

        workload.changed(::updateInput)
        visionCase.changed(::updateInput)
        fixture.setOnCheckedChangeListener { _, _ -> updateInput() }
        history.changed { selection()?.let(onSession) }
        trialChoice.changed { showTrial() }
        updateSettings()
        updateInput()
        showSession(null)
        setEnabled(true)
    }

    fun configuration(): Pair<BenchConfig, VisionTruth?>? {
        val warm = warmups.text.toString().toIntOrNull()?.takeIf { it in 0..3 }
        val repeat = repeats.text.toString().toIntOrNull()?.takeIf { it in 1..10 }
        if (warm == null || repeat == null) {
            setStatus("Warm-up은 0–3회, 반복은 1–10회로 지정하세요.")
            return null
        }
        val config = BenchConfig(
            workload(), BenchOrder.entries[order.selectedItemPosition], settings, warm, repeat,
            contextSweep.isChecked, thinkingPair.isChecked, NeedlePosition.entries[needlePosition.selectedItemPosition],
            visionCase(), fixture.isChecked,
        )
        if (config.visionCase == VisionCase.METHOD && config.workload == BenchWorkload.VISION &&
            config.order.backends.size > 1) {
            setStatus("직접 입력과 요약 비교는 단일 백엔드를 선택하세요. 최종 비교 요청은 각 이미지에서 생성한 요약을 입력으로 받습니다.")
            return null
        }
        if (config.plannedTrials > BenchConfig.MAX_TRIALS) {
            setStatus("한 세션은 최대 ${BenchConfig.MAX_TRIALS}회입니다. 현재 계획 ${config.plannedTrials}회에서 조건이나 반복 수를 줄이세요.")
            return null
        }
        val truth = if (config.workload == BenchWorkload.VISION && !config.syntheticImages) {
            val prompt = question.text.toString().trim()
            val reference = expected.text.toString().trim()
            if (prompt.isBlank() || reference.isBlank()) {
                setStatus("시각 평가의 질문과 기준 답변을 모두 입력하세요.")
                return null
            }
            VisionTruth(prompt, reference)
        } else null
        return config to truth
    }

    fun setPreview(slot: Int, bitmap: Bitmap?) {
        previews[slot].setImageBitmap(bitmap)
        previews[slot].visibility = if (bitmap == null) View.GONE else View.VISIBLE
    }

    fun clearPrivateInput() {
        question.text.clear()
        expected.text.clear()
    }

    fun setEnabled(enabled: Boolean) {
        editing = enabled
        controls.forEach { setEnabledTree(it, enabled) }
        setEnabledTree(historyActions, enabled && selection() != null)
        clearAll.isEnabled = enabled && sessions.isNotEmpty()
        stop.isEnabled = running && !stopPending
    }

    fun setRunning(value: Boolean) {
        running = value
        stopPending = false
        bar.visibility = if (value) View.VISIBLE else View.GONE
        if (value) {
            bar.progress = 0
            monitor.text = ""
            latestIndex = 0
            completedIndex = 0
        }
        setEnabled(editing)
    }

    fun markStopping() {
        stopPending = true
        stop.isEnabled = false
        setStatus("중단 요청을 받았습니다. 현재 네이티브 턴이 끝날 때까지 기다립니다.")
    }

    fun showProgress(progress: BenchProgress) {
        if (!running || progress.index < latestIndex || progress.index <= completedIndex) return
        if (latestIndex == 0 && !stopPending) setStatus("실험 진행 중입니다.")
        latestIndex = progress.index
        if (progress.completed != null) completedIndex = progress.index
        bar.max = progress.planned
        bar.progress = if (progress.completed != null) progress.index else progress.index - 1
        monitor.text = BenchmarkDashboard.progress(progress)
    }

    fun setStatus(message: String) {
        status.text = message
    }

    fun setHistory(entries: List<BenchSessionHeader>, preferred: String?) {
        val selected = preferred ?: selection()
        sessions = entries
        history.adapter = ArrayAdapter(activity, android.R.layout.simple_spinner_dropdown_item,
            sessions.map { activity.getString(R.string.benchmark_history_timestamp, it.createdUtc, it.request.workload.title) })
        if (entries.isNotEmpty()) history.setSelection(entries.indexOfFirst { it.id == selected }.coerceAtLeast(0))
        else showSession(null)
        setEnabled(editing)
    }

    fun showSession(session: BenchSession?) {
        displayed = session
        dashboard.text = session?.let(BenchmarkDashboard::summary) ?: "저장된 세션이 없습니다."
        val trials = session?.trials.orEmpty()
        trialGroup.visibility = if (trials.isEmpty()) View.GONE else View.VISIBLE
        timeline.text = BenchmarkDashboard.timeline(trials)
        trialChoice.adapter = ArrayAdapter(activity, android.R.layout.simple_spinner_dropdown_item,
            trials.map { "#${it.index} ${it.condition.backend} · ${BenchmarkDashboard.phaseLabel(it.phase)} · 세부 지표" })
        showTrial()
    }

    private fun showTrial() {
        trialDetails.text = displayed?.trials?.getOrNull(trialChoice.selectedItemPosition)
            ?.let(BenchmarkDashboard::trial).orEmpty()
    }

    private fun selection() = sessions.getOrNull(history.selectedItemPosition)?.id

    private fun workload() = BenchWorkload.entries.getOrElse(workload.selectedItemPosition) { BenchWorkload.TEXT }
    private fun visionCase() = VisionCase.entries.getOrElse(visionCase.selectedItemPosition) { VisionCase.PRESENT }

    private fun updateInput() {
        val current = workload()
        needleGroup.visibility = if (current == BenchWorkload.NEEDLE || current == BenchWorkload.SATURATION) View.VISIBLE else View.GONE
        visionGroup.visibility = if (current == BenchWorkload.VISION) View.VISIBLE else View.GONE
        imageGroup.visibility = if (photoCount > 0) View.VISIBLE else View.GONE
        fixtureInfo.visibility = if (fixture.isChecked) View.VISIBLE else View.GONE
        fixtureInfo.text = if (current == BenchWorkload.VISION) {
            "내장 기준 · ${visionCase().prompt}\n기준 답변 · ${visionCase().expected}"
        } else "고정된 도형 이미지를 사용합니다. 두 이미지 입력은 선택한 런타임과 기기에서 직접 실행합니다."
        userImages.visibility = if (fixture.isChecked) View.GONE else View.VISIBLE
        slots.forEachIndexed { index, view -> view.visibility = if (index < photoCount) View.VISIBLE else View.GONE }
        truthGroup.visibility = if (current == BenchWorkload.VISION && !fixture.isChecked) View.VISIBLE else View.GONE
    }

    private fun updateSettings() {
        val thinking = if (settings.thinking) "ON" else "OFF"
        settingsSummary.text = activity.getString(
            R.string.benchmark_generation_summary, settings.contextLength, settings.maxOutputTokens, thinking,
        )
    }

    private fun field(parent: LinearLayout, label: String, initial: String, limit: Int): EditText {
        val input = EditText(activity).apply {
            id = View.generateViewId()
            isSingleLine = true
            setText(initial)
            filters = arrayOf(InputFilter.LengthFilter(limit))
            importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO
            isSaveEnabled = false
        }
        parent.addView(text(label).apply { labelFor = input.id })
        parent.addView(input)
        return input
    }

    private fun labeledChoice(parent: LinearLayout, label: String, values: List<String>, selected: Int = 0): Spinner {
        val spinner = Spinner(activity).apply {
            id = View.generateViewId()
            adapter = ArrayAdapter(activity, android.R.layout.simple_spinner_dropdown_item, values)
            setSelection(selected)
            minimumHeight = dp(48)
            isSaveEnabled = false
        }
        parent.addView(text(label).apply { labelFor = spinner.id })
        parent.addView(spinner)
        return spinner
    }

    private fun Spinner.changed(action: () -> Unit) {
        onItemSelectedListener = object : AdapterView.OnItemSelectedListener {
            override fun onItemSelected(parent: AdapterView<*>?, view: View?, position: Int, id: Long) = action()
            override fun onNothingSelected(parent: AdapterView<*>?) = Unit
        }
    }

    private fun button(label: String, click: () -> Unit) = Button(activity).apply {
        text = label
        isAllCaps = false
        setOnClickListener { click() }
    }

    private fun check(label: String, checked: Boolean) = CheckBox(activity).apply {
        text = label
        isChecked = checked
        minHeight = dp(48)
    }

    private fun text(label: String, title: Boolean = false) = TextView(activity).apply {
        text = label
        textSize = if (title) 18f else 14f
        setPadding(0, dp(if (title) 12 else 4), 0, dp(4))
        if (title) setTypeface(null, Typeface.BOLD)
    }

    private fun column() = LinearLayout(activity).apply { orientation = LinearLayout.VERTICAL }

    private fun row(vararg buttons: Button) = LinearLayout(activity).apply {
        orientation = LinearLayout.HORIZONTAL
        buttons.forEach { addView(it, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)) }
    }

    private fun setEnabledTree(view: View, enabled: Boolean) {
        view.isEnabled = enabled
        if (view is ViewGroup && view !is AdapterView<*>) {
            for (index in 0 until view.childCount) setEnabledTree(view.getChildAt(index), enabled)
        }
    }

    private fun dp(value: Int) = (value * activity.resources.displayMetrics.density).roundToInt()
}
