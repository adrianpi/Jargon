# Jargon

<p align="center">
  <img src="https://img.shields.io/github/license/adrianpi/Jargon" alt="License">
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6" alt="Windows">
  <img src="https://img.shields.io/badge/Backend-LLVM-FF9900" alt="LLVM">
  <img src="https://img.shields.io/badge/Self--Hosting-Yes-00C853" alt="Self-Hosting">
  <img src="https://img.shields.io/badge/Status-Alpha-blue" alt="Status">
  <img src="https://img.shields.io/github/stars/adrianpi/Jargon?style=social" alt="Stars">
</p>

**Jargon** is a self-hosting compiled programming language that targets **LLVM IR** and produces native executables on Windows. The compiler (`jlc1`) is written in Jargon itself, making it a bootstrapping compiler.

## Features

- **Self-hosting** — the compiler is written in Jargon and can compile itself
- **LLVM backend** — emits LLVM IR (`.ll` files) and uses `clang-cl` for final linking
- **C-like syntax** — familiar syntax with classes, templates, enums, and more
- **Automatic Reference Counting** — automatic memory management via reference-counted objects
- **Debug support** — generates debug information compatible with standard debuggers (`-g` flag)
- **Interop with C** — direct declaration of external C functions

## Project Structure

- **JargonLib** - Jargon Compileer library implemented in C#
- **jlc0** - Jargon Compiler implemented in C#
- **Jargon** - Jargon Runtime implemented in Jargon
- **jlc1** - Jargon Compiler implemented in Jargon, compiled with jlc0
- **jlc2** - Jargon Compiler implemented in Jargon, compiled with jlc1, to check for no degradation
- **Test** - Test project
- **ComTest** - Exampe of using COM
- **WinApp** - Example of minimal Windows application

## Usage

### Options

| Flag | Description |
|------|-------------|
| `-g` | Generate debug information |
| `-k` | Keep intermediate files |
| `-o <file>` | Specify output file name |
| `-O<level>` | Optimization level (`O0`, `O1`, `O2`, `Os`, `Ofast`, `Od`, `Ot`, `Ox`) |
| `-I<dir>` | Add directory to library search path |
| `-l<library>` | Link with specified library |
| `-V` | Verbose output |
| `-h`, `--help` | Show help |

### Example

jlc1 -g Main.jr -o Test.exe

## Prerequisites

- **Windows** with Visual Studio 2022
- **LLVM / Clang** toolchain (`clang-cl` must be available)

## Building

1. Create a JARGON_LIB system variable pointing to your binary folder. i.e JARGON_LIB=C:\Jargon\x64\Debug
2. Open the solution in **Visual Studio 2022**.
3. Build the `Test` project to perform a build of all projects in the solution in the correct order.

## License

This project is available under the [MIT License](LICENSE).
