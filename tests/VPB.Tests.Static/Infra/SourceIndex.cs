using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace VPB.Tests.Static
{
    public sealed class SourceFile
    {
        public string RelativePath;
        public string Text;
        public SyntaxTree Tree;
        public List<TextSpan> InertSpans;

        public bool IsInert(int position)
        {
            for (int i = 0; i < InertSpans.Count; i++)
                if (InertSpans[i].Contains(position)) return true;
            return false;
        }

        public int LineOf(int position)
        {
            return Tree.GetText().Lines.GetLineFromPosition(position).LineNumber + 1;
        }
    }

    public static class SourceIndex
    {
        private static List<SourceFile> s_files;
        private static readonly object s_gate = new object();

        public static IReadOnlyList<SourceFile> Files
        {
            get
            {
                lock (s_gate)
                {
                    if (s_files != null) return s_files;
                    var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
                    var list = new List<SourceFile>();
                    foreach (string rel in Repo.SourceFilesOnDisk())
                    {
                        string text = File.ReadAllText(Path.Combine(Repo.Root, rel.Replace('/', Path.DirectorySeparatorChar)));
                        var tree = CSharpSyntaxTree.ParseText(SourceText.From(text), parseOptions, rel);
                        list.Add(new SourceFile
                        {
                            RelativePath = rel,
                            Text = text,
                            Tree = tree,
                            InertSpans = CollectInertSpans(tree),
                        });
                    }
                    s_files = list;
                    return s_files;
                }
            }
        }

        private static List<TextSpan> CollectInertSpans(SyntaxTree tree)
        {
            var spans = new List<TextSpan>();
            SyntaxNode root = tree.GetRoot();

            foreach (SyntaxTrivia t in root.DescendantTrivia())
            {
                switch (t.Kind())
                {
                    case SyntaxKind.SingleLineCommentTrivia:
                    case SyntaxKind.MultiLineCommentTrivia:
                    case SyntaxKind.SingleLineDocumentationCommentTrivia:
                    case SyntaxKind.MultiLineDocumentationCommentTrivia:
                    case SyntaxKind.DisabledTextTrivia:
                        spans.Add(t.FullSpan);
                        break;
                }
            }

            foreach (SyntaxToken tok in root.DescendantTokens())
            {
                switch (tok.Kind())
                {
                    case SyntaxKind.StringLiteralToken:
                    case SyntaxKind.CharacterLiteralToken:
                    case SyntaxKind.InterpolatedStringTextToken:
                    case SyntaxKind.XmlTextLiteralToken:
                        spans.Add(tok.Span);
                        break;
                }
            }

            return spans;
        }

        public static IEnumerable<T> NodesOfType<T>() where T : SyntaxNode
        {
            foreach (var f in Files)
                foreach (var n in f.Tree.GetRoot().DescendantNodes().OfType<T>())
                    yield return n;
        }
    }
}
