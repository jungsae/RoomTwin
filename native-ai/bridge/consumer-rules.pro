# Unity invokes this class by its JVM name through JNI.
-keep class com.roomtwin.nativeai.bridge.RoomTwinAiBridge {
    public static com.roomtwin.nativeai.bridge.RoomTwinAiBridge getInstance(android.content.Context);
    public java.lang.String *(...);
}
# Gson serializes these field names into the v1.0 analysis schema.
-keepclassmembers class com.roomtwin.nativeai.RoomAnalysis { <fields>; }
-keepclassmembers class com.roomtwin.nativeai.VisualObservation { <fields>; }
# LiteRT-LM 0.19.0 loads its native API from the transitive AAR.
-keep class com.google.ai.edge.litertlm.** { *; }
