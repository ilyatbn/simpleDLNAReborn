# SimpleDLNA build helpers.
#
# Needs only the .NET SDK - no Visual Studio. Works from cmd.exe, PowerShell or
# Git Bash. Everything lands in $(DIST); nothing is written next to the sources
# except the usual bin/ and obj/ intermediates.
#
# `make` is not bundled with Windows or Git for Windows. Install one of:
#   winget install ezwinports.make
#   choco install make
#   scoop install make

SOLUTION       := sdlna.sln
CONSOLE_PROJ   := sdlna/sdlna.csproj
GUI_PROJ       := SimpleDLNA/SimpleDLNA.csproj

# Override any of these on the command line, e.g.
#   make build CONFIG=Debug SELF_CONTAINED=false
CONFIG         ?= Release
DIST           ?= dist
RID            ?= win-x64
SELF_CONTAINED ?= true
VERSION_SUFFIX ?=
ARGS           ?=

# Overridable so a broken PATH can be worked around without editing this file:
#   make build DOTNET="C:\Program Files\dotnet\dotnet.exe"
DOTNET         ?= dotnet

ifeq ($(OS),Windows_NT)

# Force every recipe through cmd.exe.
#
# By default GNU make on Windows skips the shell for commands it considers
# "simple" and calls CreateProcess itself, using its own PATH search. That
# search can fail even when the executable is perfectly resolvable by the OS:
#   process_begin: CreateProcess(NULL, dotnet publish ..., ...) failed.
#   make (e=2): The system cannot find the file specified.
# Setting SHELL explicitly removes that code path, so cmd.exe resolves
# executables using the normal PATH/PATHEXT rules.
SHELL       := cmd.exe
.SHELLFLAGS := /C

EXE         := .exe
NATIVE_DIST := $(subst /,\,$(DIST))
CONSOLE_BIN := $(NATIVE_DIST)\console\sdlna$(EXE)
PS          := powershell -NoProfile -ExecutionPolicy Bypass -Command
BLANK       := echo.

# cmd has no recursive delete that tolerates missing paths, and no globbing for
# directories, so both cleanups go through PowerShell.
CLEAN_TREES  = $(PS) "Remove-Item -Recurse -Force -ErrorAction Ignore '$(DIST)',*/bin,*/obj"
MAKE_ZIPS    = $(PS) "$$s = Get-Date -Format 'yyyyMMdd-HHmmss'; Compress-Archive -Path '$(DIST)/console/*' -DestinationPath ('$(DIST)/simpledlna-' + $$s + '-console-$(RID).zip') -Force; Compress-Archive -Path '$(DIST)/gui/*' -DestinationPath ('$(DIST)/simpledlna-' + $$s + '-gui-$(RID).zip') -Force; Get-ChildItem '$(DIST)/*.zip' | Format-Table Name,Length"

else

EXE         :=
CONSOLE_BIN := ./$(DIST)/console/sdlna$(EXE)
BLANK       := echo ""

CLEAN_TREES  = rm -rf $(DIST) */bin */obj
MAKE_ZIPS    = s=$$(date -u +%Y%m%d-%H%M%S); \
               (cd $(DIST)/console && zip -qr ../simpledlna-$$s-console-$(RID).zip .); \
               (cd $(DIST)/gui     && zip -qr ../simpledlna-$$s-gui-$(RID).zip .)

endif

PUBLISH := $(DOTNET) publish -c $(CONFIG) -r $(RID) --self-contained $(SELF_CONTAINED) --nologo
ifneq ($(strip $(VERSION_SUFFIX)),)
PUBLISH += -p:VersionSuffix=$(VERSION_SUFFIX)
endif

.DEFAULT_GOAL := build
.PHONY: help build console gui restore rebuild run smoke zip clean distclean

# Text below is echoed by cmd.exe as well as sh, so it avoids the characters
# cmd treats as redirection or grouping: > < | & ( ).
help:
	@echo SimpleDLNA - make targets
	@$(BLANK)
	@echo   build      Publish both apps into $(DIST)/  [default]
	@echo   console    Publish just the CLI, to $(DIST)/console
	@echo   gui        Publish just the GUI, to $(DIST)/gui
	@echo   run        Build the CLI and run it: make run ARGS=--help
	@echo   smoke      Build the CLI and check that it starts
	@echo   zip        Build, then zip each app into $(DIST)/
	@echo   rebuild    clean, then build
	@echo   restore    NuGet restore only
	@echo   clean      Remove $(DIST)/ plus all bin and obj intermediates
	@$(BLANK)
	@echo Variables, showing the value in effect now:
	@echo   CONFIG=$(CONFIG)
	@echo   DIST=$(DIST)
	@echo   RID=$(RID)
	@echo   SELF_CONTAINED=$(SELF_CONTAINED)   false gives a small build needing the runtime
	@echo   VERSION_SUFFIX=$(VERSION_SUFFIX)   appended to the version, e.g. 20260810-2045
	@echo   DOTNET=$(DOTNET)   set to a full path if dotnet is not found

build: console gui
	@$(BLANK)
	@echo Built into $(DIST)/ : console/sdlna$(EXE) and gui/SimpleDLNA$(EXE)

console:
	$(PUBLISH) $(CONSOLE_PROJ) -o $(DIST)/console

gui:
	$(PUBLISH) $(GUI_PROJ) -o $(DIST)/gui

restore:
	$(DOTNET) restore $(SOLUTION)

rebuild: clean build

run: console
	$(CONSOLE_BIN) $(ARGS)

smoke: console
	$(CONSOLE_BIN) --version

zip: build
	@$(MAKE_ZIPS)

clean:
	-$(DOTNET) clean $(SOLUTION) -c $(CONFIG) --nologo
	@$(CLEAN_TREES)

distclean: clean
