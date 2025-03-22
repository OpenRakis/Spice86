# Contributing to Spice86

Thank you for wanting to contribute. Here are some guidelines.

## Contents

- [Contributing to Spice86](#contributing-to-spice86)
	- [Contents](#contents)
	- [Basic Information](#basic-information)
		- [Required SDK](#required-sdk)
		- [What this project should do](#what-this-project-should-do)
		- [What this project should not do](#what-this-project-should-not-do)
	- [Technical Information](#technical-information)
		- [Project Structure](#project-structure)
		- [Building the project](#building-the-project)
		- [Running tests](#running-tests)
	- [How to Contribute](#how-to-contribute)
		- [Bug Reports](#bug-reports)
		- [Contributing Code](#contributing-code)
			- [General Points](#general-points)


## Basic Information

### Required SDK

- [.NET](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

### dev environment

JetBrains Rider (free) or Visual Studio Community (free) are the best for dotnet work. 

VSCode and C# Dev Kit are free and fine too. 

### What this project should do

Emulate the hardware of the IBM PC (and compatible clones), allowing it to run games written for *real mode*.

Additionally, it should enable the reverse engineer to understand and replace the game's logic, piece by piece.

For example, new emulator APIs should be public in order to give access to all of the emulator when overriding assembly code with C# code.

### What this project should not do

* Try to emulate protected mode and protected mode games. Protected mode is out of scope of this project. Well, unless you manage to make C# overrides of assembly code work with protected mode nicely...

## Technical Information

### Project Structure

- `./src`: Contains all of the emulator, including the UI.
	- For the most part, the source code is broken down into directories representing the various pieces of hardware to be emulated. Examples:
	- `./src/Spice86.Core/Emulator/Memory/` contains the code for managing memory and monitoring memory read/write access.
	- `.src/Spice86.Core/Emulator/Functions/` contains functions tracking APIs.
	- `./src/Spice86.Core/Emulator/ReverseEngineer/` contains Reverse Engineering APIs.

- 'tests' : contains all the unit tests. They are centered around ensuring that the CPU and low level emulation and C# overrides still work. 


### Building the project

Do this where Spice86.sln is located:

```bash
$ dotnet build
```

### Running tests

Do this where Spice86.Tests.csproj is located:

```bash
$ dotnet test
```

## How to Contribute

This project works already quite well for rewriting games such as DUNE, but it is still in early development.

As such, there is a lot of code that needs to be written, especially for emulating additionnal hardware, remaining Memory features such as XMS, CPU 286/386 instruction set unit tests, DOS services, BIOS services, and low-level interrupts.

### Bug Reports

The standard issue template is used.

### Contributing Code

Pull Requests are welcome. See the [PR template](https://github.com/OpenRakis/Spice86/blob/master/.github/pull_request_template.md) for more details.

#### General Points

- Commits to be merged should pass all checks (should build, and pass tests)
- Comments are important, especially when contributing emulation code. Technical information is hard to find.
