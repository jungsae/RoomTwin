plugins {
    id("com.android.application")
}

android {
    namespace = "com.roomtwin.nativeai"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.roomtwin.nativeai"
        minSdk = 28
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0"

        ndk {
            abiFilters += "arm64-v8a"
        }
    }

    buildTypes {
        create("field") {
            initWith(getByName("debug"))
            applicationIdSuffix = ".field"
            matchingFallbacks += "debug"
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies {
    if (providers.gradleProperty("usePublishedBridge").orNull == "true") {
        implementation("com.roomtwin:native-ai:0.1.0")
    } else {
        implementation(project(":bridge"))
    }
    implementation("androidx.core:core:1.17.0")
    testImplementation("junit:junit:4.13.2")
    "fieldImplementation"("com.google.code.gson:gson:2.14.0")
    "fieldImplementation"("com.google.ai.edge.litertlm:litertlm-android:0.19.0")
}
