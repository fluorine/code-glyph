using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGlyph.Vsix.Editor
{
    /// <summary>
    /// Reads symbols from the syntax tree only. Method bodies are measured for length
    /// but their text is never put into a descriptor.
    /// </summary>
    internal static class RoslynSymbolReader
    {
        private static readonly Regex CrefTag = new Regex("<see(?:also)?\\s+cref=\"([^\"]+)\"\\s*/>", RegexOptions.Compiled);
        private static readonly Regex AnyTag = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex("\\s+", RegexOptions.Compiled);

        public static ParsedFile Parse(string text, string fileName, int tabSize)
        {
            var tree = CSharpSyntaxTree.ParseText(text);
            var source = tree.GetText();
            var symbols = new List<SymbolSpan>();
            Walk(tree.GetRoot(), -1, source, fileName, symbols);

            var columns = new int[source.Lines.Count];
            for (var i = 0; i < columns.Length; i++)
            {
                columns[i] = Columns(source.Lines[i].ToString(), tabSize);
            }
            return new ParsedFile(symbols, columns);
        }

        private static void Walk(SyntaxNode container, int parentIndex, Microsoft.CodeAnalysis.Text.SourceText source,
            string fileName, List<SymbolSpan> symbols)
        {
            foreach (var child in container.ChildNodes())
            {
                switch (child)
                {
                    case BaseNamespaceDeclarationSyntax ns:
                        Walk(ns, parentIndex, source, fileName, symbols);
                        break;
                    case BaseTypeDeclarationSyntax type:
                        {
                            var index = Add(symbols, Describe(type, SymbolKind.Type, type.Identifier.ValueText, fileName), type, parentIndex, source);
                            Walk(type, index, source, fileName, symbols);
                            break;
                        }
                    case MethodDeclarationSyntax method:
                        Add(symbols, Describe(method, SymbolKind.Method, method.Identifier.ValueText, fileName), method, parentIndex, source);
                        break;
                    case PropertyDeclarationSyntax property:
                        Add(symbols, Describe(property, SymbolKind.Property, property.Identifier.ValueText, fileName), property, parentIndex, source);
                        break;
                }
            }
        }

        private static int Add(List<SymbolSpan> symbols, SymbolDescriptor d, SyntaxNode node, int parentIndex,
            Microsoft.CodeAnalysis.Text.SourceText source)
        {
            var start = source.Lines.GetLinePosition(node.SpanStart).Line;
            var end = source.Lines.GetLinePosition(Math.Max(node.SpanStart, node.Span.End - 1)).Line;
            d.LineCount = end - start + 1;
            symbols.Add(new SymbolSpan(d, start, end, parentIndex));
            return symbols.Count - 1;
        }

        private static SymbolDescriptor Describe(SyntaxNode node, SymbolKind kind, string name, string fileName)
        {
            var d = new SymbolDescriptor { Kind = kind, Name = name, FileName = fileName };

            d.Namespace = string.Join(".", node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse().Select(n => n.Name.ToString()));
            var owner = node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
            if (owner != null) d.Container = owner.Identifier.ValueText;

            ReadLeadingTrivia(node, d);

            IEnumerable<AttributeListSyntax> attributes = Enumerable.Empty<AttributeListSyntax>();
            switch (node)
            {
                case BaseTypeDeclarationSyntax t:
                    attributes = t.AttributeLists;
                    if (t.BaseList != null)
                        foreach (var b in t.BaseList.Types) d.BaseTypes.Add(b.Type.ToString());
                    break;
                case MethodDeclarationSyntax m:
                    attributes = m.AttributeLists;
                    foreach (var p in m.ParameterList.Parameters) d.Parameters.Add(p.Identifier.ValueText);
                    break;
                case PropertyDeclarationSyntax p:
                    attributes = p.AttributeLists;
                    break;
            }
            foreach (var a in attributes.SelectMany(l => l.Attributes)) d.Attributes.Add(a.Name.ToString());
            return d;
        }

        private static void ReadLeadingTrivia(SyntaxNode node, SymbolDescriptor d)
        {
            var comments = new List<string>();
            foreach (var trivia in node.GetLeadingTrivia())
            {
                switch (trivia.Kind())
                {
                    case SyntaxKind.SingleLineDocumentationCommentTrivia:
                    case SyntaxKind.MultiLineDocumentationCommentTrivia:
                        d.Summary = SummaryOf(trivia);
                        break;
                    case SyntaxKind.SingleLineCommentTrivia:
                        comments.Add(trivia.ToString().TrimStart('/').Trim());
                        break;
                    case SyntaxKind.MultiLineCommentTrivia:
                        comments.Add(trivia.ToString().Trim('/', '*', ' ', '\r', '\n', '\t'));
                        break;
                }
            }
            d.Comments = Clean(string.Join(" ", comments));
        }

        private static string SummaryOf(SyntaxTrivia trivia)
        {
            if (!(trivia.GetStructure() is DocumentationCommentTriviaSyntax doc)) return "";
            var summary = doc.ChildNodes().OfType<XmlElementSyntax>()
                .FirstOrDefault(e => e.StartTag.Name.ToString() == "summary");
            var raw = summary != null ? summary.Content.ToString() : doc.Content.ToString();
            raw = CrefTag.Replace(raw, "$1");
            raw = AnyTag.Replace(raw, " ");
            return Clean(raw.Replace("///", " ").Replace("*", " "));
        }

        private static string Clean(string s) => Spaces.Replace(s ?? "", " ").Trim();

        private static int Columns(string line, int tabSize)
        {
            var col = 0;
            foreach (var c in line)
            {
                col = c == '\t' ? col + tabSize - (col % tabSize) : col + 1;
            }
            return col;
        }
    }
}
