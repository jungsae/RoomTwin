pluginManagement {
    repositories {
        google()
        mavenCentral()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
        if (providers.gradleProperty("usePublishedBridge").orNull == "true") {
            maven {
                url = uri("bridge/build/unity-repository")
                content { includeModule("com.roomtwin", "native-ai") }
            }
        }
    }
}

rootProject.name = "RoomTwinNativeAi"
include(":app", ":bridge")
