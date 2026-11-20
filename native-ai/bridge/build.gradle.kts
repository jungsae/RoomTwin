plugins {
    id("com.android.library")
    id("maven-publish")
}

android {
    namespace = "com.roomtwin.nativeai.bridge"
    compileSdk = 36

    defaultConfig {
        minSdk = 28
        consumerProguardFiles("consumer-rules.pro")
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    publishing { singleVariant("release") }
}

dependencies {
    implementation("com.google.code.gson:gson:2.14.0")
    implementation("com.google.ai.edge.litertlm:litertlm-android:0.19.0")
    testImplementation("junit:junit:4.13.2")
}

publishing {
    publications {
        register<MavenPublication>("release") {
            groupId = "com.roomtwin"
            artifactId = "native-ai"
            version = "0.1.0"
            afterEvaluate { from(components["release"]) }
        }
    }
    repositories {
        maven {
            name = "Unity"
            url = uri(layout.buildDirectory.dir("unity-repository"))
        }
    }
}
