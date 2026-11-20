package com.roomtwin.nativeai

import com.google.gson.JsonParser
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class VisionLabProtocolTest {
    @Test
    fun parsesThreeEvidenceBackedQuestionsAndScoresEachOnlyOnce() {
        val quiz = VisionLabJson.quiz(validQuiz()) as LabQuizResult.Questions
        val session = LabQuizSession(quiz.questions)
        assertEquals(3, quiz.questions.size)
        assertTrue(quiz.questions.first().evidence.isNotEmpty())
        assertEquals(true, session.answer(0, 0))
        assertEquals(false, session.answer(1, 0))
        assertNull(session.answer(1, 1))
        assertNull(session.answer(2, 4))
        assertNull(session.answer(3, 0))
        assertEquals(2, session.answeredCount)
        assertEquals(1, session.score)
        assertEquals(true, session.answer(2, 2))
        assertEquals(2, session.score)
    }

    @Test
    fun rejectsIncorrectCountsAndDuplicates() {
        assertNull(VisionLabJson.quiz("""{"questions":[${question(0)}],"reason":""}"""))
        assertNull(VisionLabJson.quiz("""{"questions":[${question(0)},${question(0)},${question(0)}],"reason":""}"""))
        assertNull(VisionLabJson.quiz(validQuiz().replace("\"파랑\",\"초록\"", "\"파랑\",\"파랑\"")))
    }

    @Test
    fun validatesAnswerTypeRangeAndEvidence() {
        assertNull(VisionLabJson.quiz(validQuiz().replace("\"answerIndex\":0", "\"answerIndex\":4")))
        assertNull(VisionLabJson.quiz(validQuiz().replace("\"answerIndex\":0", "\"answerIndex\":\"0\"")))
        assertNull(VisionLabJson.quiz(validQuiz().replace("\"사진에서 보입니다\"", "\"\"")))
    }

    @Test
    fun acceptsFencedJsonAndRejectsTrailingOrTruncatedContent() {
        assertNotNull(VisionLabJson.quiz("```json\n${validQuiz()}\n```"))
        assertNull(VisionLabJson.quiz(validQuiz() + " 모델 설명"))
        assertNull(VisionLabJson.quiz(validQuiz().dropLast(2)))
        assertNull(VisionLabJson.quiz("{questions:[],reason:'text'}"))
    }

    @Test
    fun distinguishesInsufficientEvidenceFromInvalidJson() {
        val result = VisionLabJson.quiz("""{"questions":[],"reason":"물건이 보이지 않습니다."}""")
        assertEquals(LabQuizResult.NoEvidence("물건이 보이지 않습니다."), result)
        assertNull(VisionLabJson.quiz("""{"questions":[],"reason":""}"""))
    }

    @Test
    fun onlyAcceptsCategoricalLocationsForVisibleObjects() {
        val found = VisionLabJson.finding("""{"status":"found","region":"left","nearby":"책상 옆","evidence":"빨간 컵"}""")
        assertEquals(LabPresence.FOUND, found?.presence)
        assertEquals(LabRegion.LEFT, found?.region)
        val absent = VisionLabJson.finding("""{"status":"not_observed","region":"unknown","nearby":"","evidence":"물체 일부만 보임"}""")
        assertEquals(LabPresence.NOT_OBSERVED, absent?.presence)
        assertNull(VisionLabJson.finding("""{"status":"uncertain","region":"center","nearby":"","evidence":"가림"}"""))
        assertNull(VisionLabJson.finding("""{"status":"found","region":[0,1,2,3],"nearby":"","evidence":"가림"}"""))
    }

    @Test
    fun retainsUnreadableTextAndRejectsTranslationWithoutSource() {
        val unreadable = VisionLabJson.translation("""{"sourceText":"","translatedText":"","unreadable":["간판이 흐림"]}""")
        assertEquals(listOf("간판이 흐림"), unreadable?.unreadable)
        assertNull(VisionLabJson.translation("""{"sourceText":"","translatedText":"Coffee","unreadable":["흐림"]}"""))
        val readable = VisionLabJson.translation("""{"sourceText":"커피","translatedText":"Coffee","unreadable":[]}""")
        assertEquals("커피", readable?.original)
        assertEquals("Coffee", readable?.translated)
    }

    @Test
    fun schemasAreJsonAndComparisonKeepsOrderedEvidence() {
        for (schema in listOf(VisionLabSchemas.quiz, VisionLabSchemas.find, VisionLabSchemas.translation)) {
            assertTrue(JsonParser.parseString(schema).isJsonObject)
        }
        val summary = VisionLabPrompts.compareSummaries("red cup", "blue cup")
        assertTrue(summary.indexOf("Before") < summary.indexOf("red cup"))
        assertTrue(summary.indexOf("red cup") < summary.indexOf("After"))
        assertTrue(summary.indexOf("After") < summary.indexOf("blue cup"))
        assertTrue(VisionLabPrompts.detective("컵은 어디에?").contains("직접 보이는 사실"))
        assertFalse(VisionLabPrompts.detective(null).contains("후속 질문:"))
    }

    @Test
    fun translationPromptUsesRequestedLanguageWithoutKoreanAnswerRule() {
        val prompt = VisionLabPrompts.translate("영어")
        assertTrue(prompt.contains("번역 대상 언어: 영어"))
        assertFalse(prompt.contains("한국어로 답한다"))
    }

    private fun validQuiz() = """{"questions":[${(0..2).joinToString(",") { question(it) }}],"reason":""}"""

    private fun question(index: Int) = """
        {"question":"보이는 색 $index?","options":["빨강","파랑","초록","검정"],"answerIndex":$index,
        "evidence":"사진에서 보입니다","explanation":"다른 색은 해당 물건에서 보이지 않습니다."}
    """.trimIndent()
}
