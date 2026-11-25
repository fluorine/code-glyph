<p align="center"><img src="assets/icon.png" alt="CodeGlyph icon" width="128"></p>

# CodeGlyph

CodeGlyph is a Visual Studio extension. It draws one subtle icon behind the code, at the
right of each C# class, method and property. The icon shows what the symbol does, for
example orders, email, queues or security. The icon is a reading guide.

<p align="center"><img width="341" alt="CodeGlyph screenshot" src="https://github.com/user-attachments/assets/bcc035dc-1176-4a0c-9953-7b9240bdfbac" /></p>

This project is vibed. Expect rough edges. Pull requests and bug reports are welcome.

https://github.com/user-attachments/assets/8d75d32d-9804-46a0-9564-79297f83e0fe

## Install

[Install CodeGlyph from the Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=fluorinestudios.codeglyph),
or open Extensions > Manage Extensions in Visual Studio and search for "CodeGlyph".
You can also download the `.vsix` file from the [releases page](https://github.com/fluorine/code-glyph/releases).

## VS Code version

The folder [vscode/](vscode) holds a VS Code extension. [Install it from the Marketplace](https://marketplace.visualstudio.com/items?itemName=fluorinestudios.codeglyph-vscode), or search for "CodeGlyph" in the Extensions view. It works with any language that has a language extension with symbols (TypeScript, Python, Go, Java, Rust, C# and more). It uses the same icon set, rules and cache design. See [vscode/README.md](vscode/README.md).

The Visual Studio extension in src/ stays C# only.

## Rules

- The icon never covers code. If the free space is too small, CodeGlyph shows no icon.
- A generic icon is worse than no icon. The model must answer "none" when it is not sure.
- The model reads names and comments. It never reads method bodies.
- CodeGlyph generates each icon on demand and keeps the answer in a cache.
  Equal symbols reuse the same icon in all files and solutions.

## How it works

1. Roslyn reads the syntax tree and builds a descriptor for each symbol.
   The descriptor holds the name, container, XML summary, comments, base types and attributes.
2. The cache gives the answer if the descriptor is known.
3. For unknown descriptors, one batch request goes to the LLM provider.
   The model picks one icon from the bundled [Lucide](https://lucide.dev) set, or answers "none".
4. A WPF adornment layer behind the text draws the icon. The color follows the editor theme.

## Providers

| Provider | Needs |
|---|---|
| Claude API | `ANTHROPIC_API_KEY` or a key in the options page |
| OpenAI | `OPENAI_API_KEY` or a key in the options page |
| Claude Code CLI | The `claude` command in `PATH` |

## Choosing the icon

The icon comes from the first source that has an answer:

1. `.codeglyph.json` (see below).
2. A short word list. A name like `SendWelcomeEmail` has one known word (email), so it gets the mail icon with no model call. Turn this off with "Use local word list".
3. The cache of earlier model answers.
4. The model. It also gets the last two folder names and the icons your project already uses for the same words.

A method name that starts with a verb (Create, Delete, Get, Send, Validate, Update, Sync) gets a small badge in the corner of its icon. Turn this off with "Verb badges".

## Icon sensitivity

The model rates how sure it is of each icon. "Icon sensitivity" (Tools → Options → CodeGlyph → Provider)
sets how sure it must be before CodeGlyph shows the icon.

| Level | Shows | Use it when |
|---|---|---|
| Strict | Only close matches. Fewest icons. | You want only icons that are clearly right. |
| Balanced (default) | Icons that a developer would accept. | Most projects. |
| Generous | More icons, with looser matches. | You want an icon on most symbols. |

A change shows at once, with no new model calls. "Exact confidence limit" (Advanced) replaces the level with a number from 0.05 to 1.

With the "Sticky" vertical behavior, an icon stays on screen while any part of its block is visible.
It shrinks as the block scrolls away and goes only when almost none of the block is left.

## Change or hide an icon

Put a `.codeglyph.json` file in your repository. CodeGlyph uses the nearest file above the source file.
The Visual Studio extension reads this file. The VS Code extension reads and writes it
(command "CodeGlyph: Change icon of symbol at cursor...", or the links in the hover).

```json
{
  "overrides": [
    { "name": "CancelOrder", "container": "OrderService", "kind": "Method", "icon": "shopping-cart" },
    { "name": "Helper", "icon": null }
  ]
}
```

`container` and `kind` (`Type`, `Method` or `Property`) are optional. An entry with more fields wins.
`"icon": null` hides the icon. Icon names are from the [Lucide](https://lucide.dev/icons) set.

## Appearance options

Color mode (theme, custom, by symbol kind, by icon), opacity, render style
(outline, filled, duotone), stroke width, horizontal and vertical alignment,
sticky or scrolling behavior, size limits, margins, and the symbol kinds to show.

## Similar tools

So far, no other tool found draws a semantic icon per symbol behind the code. These tools are the closest.
The status of each tool was not checked in detail.

**Background images in the editor (decoration, not meaning)**

- [ClaudiaIDE](https://github.com/buchizo/ClaudiaIDE): Visual Studio. Shows images behind the editor.
- [MoeIDE X](https://gitee.com/dotnetfans/MoeIDE-X): Visual Studio. Background image for the whole window.
- [SubfolderWatermarks](https://github.com/tan00001/SubfolderWatermarks): Visual Studio.
  Shows a text watermark over the editor for files in chosen project folders.
- [vscode-background](https://www.github.com/shalldie/vscode-background): VS Code.
  Background image, in front of or behind the code.
- [KatsuteDev/Background](https://github.com/KatsuteDev/Background): VS Code. Window and editor images.

**Semantic information in the editor (meaning, but not images)**

- [JetBrains gutter icons](https://jetbrains.com/guide/java/tips/gutter-icons): icons for overrides, implementations and more.
- [CodeLens](https://docs.devsense.com/navigation/codelens): text above symbols with references and implementations.
- [Visual Studio Inheritance Margin](https://marketplace.visualstudio.com/vsgallery/3a55d546-0cb2-4991-a002-e6be8ce09f08):
  inheritance glyphs in the glyph margin.
- [Moji](https://www.producthunt.com/p/moji/moji): VS Code. Visual symbols for keywords.

**Research**

- [Helveg](https://arxiv.org/pdf/2507.10244v1): glyph per code element in C# diagrams.
- [Visual augmentation of source code editors](https://ar5iv.org/html/1804.02074): survey of editor overlays.

## Build

Requirements: Visual Studio 2022 17.8 or later (its MSBuild builds the VSIX) and .NET SDK 10.
The VSIX build tools come from NuGet, so the "Visual Studio extension development"
workload is not required to build. It is required to debug in the experimental instance.

```bash
dotnet test tests/CodeGlyph.Core.Tests
```

Build the VSIX with Visual Studio MSBuild (`dotnet build` cannot build it):

```bash
msbuild src/CodeGlyph.Vsix/CodeGlyph.Vsix.csproj /restore /p:Configuration=Release
```

The package is `src/CodeGlyph.Vsix/bin/Release/net48/CodeGlyph.Vsix.vsix`.
Double-click it to install. To debug, open the solution in Visual Studio and press F5
on the `CodeGlyph.Vsix` project, or run `devenv.exe /rootsuffix Exp`.

To test with a real provider, set `CODEGLYPH_LIVE` to `cli`, `anthropic` or `openai`
and run `dotnet test --filter LiveSmoke`.

Settings are in Tools > Options > CodeGlyph. Logs are in the Output window, pane "CodeGlyph".

## License

MIT. See `LICENSE`. The Lucide icons keep their own ISC license in `THIRD-PARTY-NOTICES.md`.

## Icon set

Icons come from Lucide (ISC license). `tools/PackIcons` packs them into
`src/CodeGlyph.Core/Resources/icons.json.gz`. See `THIRD-PARTY-NOTICES.md`.
