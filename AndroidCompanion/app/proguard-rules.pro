# Add project specific ProGuard rules here.

# Ktor server + Netty warnings
-dontwarn io.ktor.**
-dontwarn io.netty.**
-dontwarn kotlin.reflect.**

# Gson — keep all DTO model classes annotated with @Keep or in model packages
-keep @androidx.annotation.Keep class * { *; }
-keepclassmembers class * {
    @com.google.gson.annotations.SerializedName <fields>;
}
-keep class com.aeliavision.androiddeck.feature.contacts.model.** { *; }
-keep class com.aeliavision.androiddeck.feature.backup.model.** { *; }

# Hilt & Coroutines
-keepattributes *Annotation*,Signature
