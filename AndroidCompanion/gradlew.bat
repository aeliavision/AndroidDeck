@rem
@rem Minimal Gradle wrapper script for Windows.
@rem Uses gradle/wrapper/gradle-wrapper.jar already checked into the repo.
@echo off
setlocal

set DIR=%~dp0

if not exist "%DIR%gradle\wrapper\gradle-wrapper.jar" (
  echo Missing gradle-wrapper.jar under gradle\wrapper\
  exit /b 1
)

if defined JAVA_HOME (
  set JAVA_EXE=%JAVA_HOME%\bin\java.exe
) else (
  set JAVA_EXE=java.exe
)

"%JAVA_EXE%" -classpath "%DIR%gradle\wrapper\gradle-wrapper.jar" org.gradle.wrapper.GradleWrapperMain %*
exit /b %ERRORLEVEL%
