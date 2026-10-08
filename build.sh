#!/usr/bin/env bash
# Build VrFsim Controller Connect and its installer into dist/:
#   dist/ControllerConnect.exe                     the program (portable)
#   dist/VrFsim-Controller-Connect-Setup.exe       one-file installer (desktop + Start menu shortcuts, uninstall)
# Uses only what ships with Windows: the .NET Framework C# compiler and PowerShell. Run from Git Bash.
set -eu
ROOT="$(cd "$(dirname "$0")" && pwd)"
VERSION="$(cat "$ROOT/VERSION")"
CSC="/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
DIST="$ROOT/dist"
WORK="$ROOT/build"
w() { cygpath -w "$1"; }

rm -rf "${WORK:?}"
mkdir -p "$DIST" "$WORK/payload"
cat > "$WORK/AppInfo.cs" <<CS
namespace ControllerConnect { static class BuildInfo { public const string Version = "$VERSION"; } }
CS

# 1. The program.
"$CSC" -nologo -target:winexe -optimize+ -platform:anycpu \
  -out:"$(w "$DIST/ControllerConnect.exe")" \
  -win32icon:"$(w "$ROOT/assets/icon.ico")" \
  -win32manifest:"$(w "$ROOT/installer/app.manifest")" \
  -resource:"$(w "$ROOT/assets/logo.png")",logo.png \
  -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll \
  "$(w "$ROOT/src")\*.cs" "$(w "$WORK/AppInfo.cs")"

# 2. The installer, with the program inside.
cp "$DIST/ControllerConnect.exe" "$WORK/payload/"
powershell.exe -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; [IO.Compression.ZipFile]::CreateFromDirectory('$(w "$WORK/payload")', '$(w "$WORK/payload.zip")', [IO.Compression.CompressionLevel]::Optimal, \$false)"
cat > "$WORK/SetupInfo.cs" <<CS
namespace AppSetup
{
    static class BuildInfo
    {
        public const string AppName = "VrFsim Controller Connect";
        public const string ExeName = "ControllerConnect.exe";
        public const string UninstallId = "VrFsimControllerConnect";
        public const string Version = "$VERSION";
        public const string RepoUrl = "https://github.com/guyka2212/controller-connect";
        public const string Description = "Use a controller plugged into this PC with VrFsim running on a Meta Quest";
        public const string Intro = "For laptops that can't run Quest Link: VrFsim runs on the Quest, and the controller plugged "
            + "into this PC is sent to it over the headset's USB cable. If your PC can run Link, use the normal VrFsim installer instead.";
    }
}
CS
"$CSC" -nologo -target:winexe -optimize+ -platform:anycpu \
  -out:"$(w "$DIST/VrFsim-Controller-Connect-Setup.exe")" \
  -win32icon:"$(w "$ROOT/assets/icon.ico")" \
  -win32manifest:"$(w "$ROOT/installer/app.manifest")" \
  -resource:"$(w "$WORK/payload.zip")",payload.zip \
  -resource:"$(w "$ROOT/assets/logo.png")",logo.png \
  -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll \
  "$(w "$ROOT/installer/Setup.cs")" "$(w "$WORK/SetupInfo.cs")"

ls -la "$DIST"
echo "CONTROLLER CONNECT: built version $VERSION"
