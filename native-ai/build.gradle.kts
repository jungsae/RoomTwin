buildscript {
    repositories {
        mavenCentral()
    }
    dependencies {
        // The LiteRT-LM 0.19.0 AAR uses Kotlin 2.4 metadata.
        classpath("org.jetbrains.kotlin:kotlin-gradle-plugin:2.4.0")
    }
}

plugins {
    id("com.android.application") version "9.1.1" apply false
    id("com.android.library") version "9.1.1" apply false
}
