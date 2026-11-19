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

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies {
    implementation("com.google.ai.edge.litertlm:litertlm-android:0.19.0")
}
