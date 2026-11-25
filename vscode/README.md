# CodeGlyph for VS Code

CodeGlyph draws one subtle icon behind your code, next to each function, class and property. The icon shows what the symbol does, for example orders, email, queues or security. It works in any language that has a language extension with symbols: TypeScript, JavaScript, Python, Go, Java, C#, Rust, C and C++, and more.

This project is vibed. An AI assistant wrote most of the code, and a person reviews and tests it. Expect rough edges.

## Install

[Install CodeGlyph from the Marketplace](https://marketplace.visualstudio.com/items?itemName=fluorinestudios.codeglyph-vscode), or open the Extensions view in VS Code (Ctrl+Shift+X) and search for "CodeGlyph".

## Rules

- The icon sits behind the text. It never hides code or selection.
- No icon is better than a generic icon. The model answers "none" when it is not sure.
- The model reads names, signatures and comments. It never reads code bodies.
- Each answer is cached on your disk. The same symbol gets the same icon in all files.

## How it works

1. VS Code gives the symbols of the file (the same list as the Outline view).
2. The cache gives the icon if the symbol is known.
3. For new symbols, one request goes to your AI provider. The model picks one icon from the bundled [Lucide](https://lucide.dev) set, or answers "none".
4. The icon is drawn behind the code, at a fixed text column.

## Providers

- Claude API: run "CodeGlyph: Set Claude API key", or set `ANTHROPIC_API_KEY`.
- OpenAI API (or any OpenAI-compatible endpoint): run "CodeGlyph: Set OpenAI API key", or set `OPENAI_API_KEY`.
- Claude Code CLI: the `claude` command in your `PATH`.

Keys are kept in VS Code secret storage. Symbol names, signatures and comments go to the provider you choose. Code bodies never go. Provider calls wait 15 seconds after your last edit.

## Change or hide an icon

- Hover the first line of a symbol. The hover shows where the icon came from, with links to change or hide it.
- Or run "CodeGlyph: Change icon of symbol at cursor..." from the Command Palette. You can search all icons, hide the icon, or go back to the automatic icon.
- The choice is saved in `.codeglyph.json` in your workspace. Commit it, and your team sees the same icons. The Visual Studio extension reads the same file.
- The "CodeGlyph" view in the Explorer lists the icons of the active file. Click a row to go to the symbol.

```json
{
  "overrides": [
    { "name": "CancelOrder", "container": "OrderService", "kind": "Method", "icon": "shopping-cart" },
    { "name": "Helper", "icon": null }
  ]
}
```

`container` and `kind` (`Type`, `Method` or `Property`) are optional. An entry with more fields wins. `"icon": null` hides the icon.

## Icon sensitivity

`codeglyph.iconSensitivity` sets how sure the model must be before an icon is shown: `strict` (only close matches, fewest icons), `balanced` (default) or `generous` (more icons, looser matches). A change shows at once, with no new model calls. The advanced setting `codeglyph.minConfidence` replaces the level with an exact number from 0 to 1.

## Icon choice

The icon comes from `.codeglyph.json`, then from a short word list (a name with the word Email gets the mail icon with no model call), then from the cache, then from the model. Turn the word list off with `codeglyph.useLocalWords`. A method name that starts with a verb (Create, Delete, Get, Send, Validate, Update, Sync) gets a small badge. Turn it off with `codeglyph.verbBadges`. Set `codeglyph.colorMode` to `byIcon` to give each icon its own stable color.

## Settings

Search for "codeglyph" in Settings. You can change the color mode, opacity, style, stroke width, column, icon height, symbol kinds, and the languages to skip. The Output channel "CodeGlyph" shows tokens and cost for each call. The command "CodeGlyph: Show usage" shows the session total.

The icon starts at the column in `codeglyph.column` (default 100). VS Code cannot anchor an icon to the right edge of the window. Lower the column if your editor is narrow.

## Source

https://github.com/fluorine/code-glyph (MIT). The Lucide icons keep their ISC license in `THIRD-PARTY-NOTICES.md`.
