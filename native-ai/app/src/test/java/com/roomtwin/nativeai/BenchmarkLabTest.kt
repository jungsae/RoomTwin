package com.roomtwin.nativeai

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.ByteArrayOutputStream
import java.io.File
import java.util.UUID
import java.util.concurrent.CancellationException

class BenchmarkLabTest {
    @get:Rule val temporary = TemporaryFolder()

    @Test fun selectedBackendOrderAndConditionsAreStable() {
        val config = config(BenchWorkload.TEXT).copy(
            order = BenchOrder.GPU_CPU, contextSweep = true, thinkingPair = true,
        )
        val conditions = config.conditions()
        assertEquals(8, conditions.size)
        assertEquals(InferenceBackend.GPU, conditions.first().backend)
        assertEquals(InferenceBackend.CPU, conditions.last().backend)
        assertEquals(listOf(2048, 4096), conditions.map { it.contextLength }.distinct())
        assertEquals(listOf(false, true), conditions.map { it.thinking }.distinct())
        assertEquals(40, config.plannedTrials)
        assertFalse(config.needsMobileConfirmation)
        val extended = config.copy(generation = config.generation.copy(contextLength = 8192))
        assertTrue(extended.needsMobileConfirmation)
        assertTrue(extended.conditions().any { it.contextLength == 8192 })
    }

    @Test fun multiImageMethodReportsTwoDistinctConditions() {
        val config = config(BenchWorkload.VISION).copy(visionCase = VisionCase.METHOD)
        assertEquals(2, config.photoCount)
        assertEquals(listOf(BenchMethod.DIRECT, BenchMethod.SUMMARIES), config.conditions().map { it.method })
        assertTrue(config.needsMobileConfirmation)
        assertFalse(config(BenchWorkload.TWO_IMAGES).copy(warmUps = 0, repeats = 3).needsMobileConfirmation)
    }

    @Test fun reportsNearestRankP95AndPopulationDeviation() {
        val stats = requireNotNull(BenchStats.of(listOf(40, 10, 30, 20)))
        assertEquals(4, stats.count)
        assertEquals(25.0, stats.mean, 0.001)
        assertEquals(25.0, stats.median, 0.001)
        assertEquals(40L, stats.p95)
        assertEquals(11.18034, stats.standardDeviation, 0.001)
        assertEquals(10L, stats.minimum)
        assertEquals(40L, stats.maximum)
        assertNull(BenchStats.of(emptyList()))
    }

    @Test fun needlePositionAndSaturationInputsVaryWithoutChangingAnswer() {
        val inputs = NeedlePosition.entries.map { BenchRecipes.needle(2048, 65, it) }
        val positions = inputs.map { it.indexOf("CINDER-9742") }
        assertTrue(positions[0] < positions[1] && positions[1] < positions[2])
        assertEquals(1, inputs.map(String::length).distinct().size)
        val config = config(BenchWorkload.SATURATION)
        val recipes = config.conditions().map { BenchRecipes.build(config, it, null) }
        assertEquals(listOf(30, 60, 90, 110), config.conditions().map { it.loadPercent })
        assertEquals(listOf("CINDER-9742"), recipes.map { it.expected }.distinct())
        val sizes = recipes.map { it.turns.single().text.length }
        assertTrue(sizes.zipWithNext().all { (a, b) -> a < b })
    }

    @Test fun conflictUsesMostRecentReference() {
        val config = config(BenchWorkload.CONFLICT)
        val recipe = BenchRecipes.build(config, config.conditions().single(), null)
        assertEquals(3, recipe.turns.size)
        assertEquals("ORCHID-6209", recipe.expected)
        assertTrue(BenchGrading.correct("  orchid-6209.  ", requireNotNull(recipe.expected), json = false))
        assertFalse(BenchGrading.correct("FALCON-3851", requireNotNull(recipe.expected), json = false))
    }

    @Test fun visionEvaluationUsesTheProvidedReferenceAndStrictJson() {
        val config = config(BenchWorkload.VISION).copy(syntheticImages = false, visionCase = VisionCase.CHANGE)
        val recipe = BenchRecipes.build(config, config.conditions().single(), VisionTruth("How many?", "２"))
        assertEquals(listOf(0, 1), recipe.turns.single().imageIndexes)
        assertEquals("２", recipe.expected)
        assertTrue(BenchGrading.correct("""{"answer":"2"}""", requireNotNull(recipe.expected), json = true))
        assertNull(BenchGrading.jsonAnswer("""{"answer":2}"""))
        assertNull(BenchGrading.jsonAnswer("""{"answer":"2","reason":"visible"}"""))
        assertNull(BenchGrading.jsonAnswer("""{"answer":"2","answer":"9"}"""))
        assertNull(BenchGrading.jsonAnswer("model says 2"))
    }

    @Test fun stopKeepsTheFirstReasonAndRejectsAnotherTurn() {
        val control = BenchControl()
        control.check()
        control.stop(BenchEnd.THERMAL_LIMIT)
        control.stop()
        assertEquals(BenchEnd.THERMAL_LIMIT, control.reason)
        try {
            control.check()
            throw AssertionError("A subsequent turn should not start")
        } catch (_: CancellationException) {
            assertEquals(BenchEnd.THERMAL_LIMIT, control.reason)
        }
    }

    @Test fun appendJournalSurvivesPartialRecordAndExportsRedactedData() {
        val directory = temporary.newFolder()
        val id = UUID.randomUUID().toString()
        val store = BenchStore(directory)
        val header = BenchSessionHeader(
            id, "2026-01-01T00:00:00Z", "0123456789", "0.1.0",
            BenchModelIdentity("gemma", 2L, 3L), "Test, \"Device\"", "Android test", "0.19.0",
            BenchRequestRecord(BenchWorkload.VISION, BenchOrder.CPU, 2048, 512, .8, 40, .9, false, true,
                1, 3, false, false, NeedlePosition.MIDDLE, VisionCase.OCR, 5, 1, "user_image"),
        )
        val resources = BenchResources(10L, 20L, null, null, null, null, null, null)
        val trial = BenchTrial(
            1, config(BenchWorkload.TEXT).conditions().single(), BenchPhase.COLD, BenchOutcome.SUCCESS, null,
            123, resources, resources, 10L, 20L, null,
            listOf(BenchTurn(1, 50, 1, 120, 75, NativeTurnMetrics(outputTokens = 8))), true,
        )
        store.begin(header).use { it.trial(trial) }
        File(directory, "$id.jsonl").appendText("{\"type\":")
        assertEquals(listOf(header), store.headers())
        val session = store.session(id)
        assertEquals(BenchEnd.INCOMPLETE, session.end)
        assertEquals(listOf(trial), session.trials)
        assertEquals(8, session.trials.single().outputTokens)
        val csv = ByteArrayOutputStream().also { store.export(id, true, it) }.toString(Charsets.UTF_8.name())
        assertTrue(csv.contains("\"Test, \"\"Device\"\"\""))
        assertTrue(csv.contains("actual_ttft_ms"))
        assertTrue(csv.contains("N/A"))
        val jsonl = ByteArrayOutputStream().also { store.export(id, false, it) }.toString(Charsets.UTF_8.name())
        assertTrue(jsonl.contains("\"actualTtftMillis\":null"))
        assertTrue(jsonl.contains("\"gpuDedicatedMemoryBytes\":null"))
        assertFalse(jsonl.contains("content://"))
        assertFalse(jsonl.contains("\"systemPrompt\":"))
        assertTrue(store.delete(id))
        assertEquals(0, store.deleteAll())
    }

    private fun config(workload: BenchWorkload) = BenchConfig(workload, BenchOrder.CPU, PlaygroundSettings(), 1, 3)
}
