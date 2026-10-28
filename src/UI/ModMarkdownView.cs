using System;
using System.Collections.Generic;
using Cthangover.Core.Mods;
using Cthangover.Core.UI.Theming;
using Godot;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Mods.ModManager
{
    /// <summary>
    /// CommonMark rendered through Godot's rich-text API, never as HTML or
    /// interpolated BBCode. Images come from the same provider as the document;
    /// percentage widths keep them inside the page when the window is resized.
    /// </summary>
    public partial class ModMarkdownView : RichTextLabel
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();
        private readonly Dictionary<string, ImageTexture> textures = new(StringComparer.Ordinal);
        private IModFileProvider provider;
        private ModDocument document;
        private bool connected;
        private FontFile codeFont;

        /// <summary>Replaces the document and releases images owned by the previous page.</summary>
        public void Display(IModFileProvider source, ModDocument value)
        {
            Clear();
            ReleaseTextures();
            provider = source;
            document = value;
            if (!connected)
            {
                MetaClicked += OnLinkClicked;
                connected = true;
                AddThemeColorOverride("default_color", UiTokens.Body);
                AddThemeFontSizeOverride("normal_font_size", UiTokens.FontBody);
            }
            if (value != null)
            {
                EnsureCodeFont();
                RenderBlocks(Markdown.Parse(value.Text, Pipeline), 0);
            }
        }

        public override void _ExitTree()
        {
            Clear();
            ReleaseTextures();
            provider = null;
            document = null;
            ReleaseCodeFont();
        }

        public override void _Notification(int what)
        {
            // Detached preview/test controls may be freed without entering a tree.
            if (what == NotificationPredelete)
                ReleaseCodeFont();
        }

        private void EnsureCodeFont()
        {
            if (codeFont != null || !ModRegistry.Instance.AllDiscoveredMods.TryGetValue("mod_manager", out var owner))
                return;
            var bytes = owner.FileProvider?.ReadFileBinary("fonts/DejaVuSansMono.ttf");
            if (bytes == null)
                return;
            // Read bytes through the provider so the font also works in ZIP mods,
            // without Godot import files or a system-installed monospace font.
            codeFont = new FontFile { Data = bytes };
            AddThemeFontOverride("mono_font", codeFont);
        }

        private void ReleaseCodeFont()
        {
            if (codeFont == null)
                return;
            RemoveThemeFontOverride("mono_font");
            codeFont.Dispose();
            codeFont = null;
        }

        private void ReleaseTextures()
        {
            foreach (var texture in textures.Values)
                texture.Dispose();
            textures.Clear();
        }

        private void RenderBlocks(ContainerBlock blocks, int depth)
        {
            if (depth > 32)
                return;
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case HeadingBlock heading:
                        PushFontSize(Math.Max(UiTokens.FontBody + 2, 30 - heading.Level * 2));
                        PushBold();
                        RenderInlines(heading.Inline, depth + 1);
                        Pop();
                        Pop();
                        break;
                    case ParagraphBlock paragraph:
                        RenderInlines(paragraph.Inline, depth + 1);
                        break;
                    case CodeBlock code:
                        PushMono();
                        PushBgcolor(UiTokens.Surface0);
                        foreach (var span in ModCodeSyntax.Tokenize(code is FencedCodeBlock fence ? fence.Info : null, code.Lines.ToString()))
                        {
                            if (span.Kind.Length > 0)
                                PushColor(CodeColor(span.Kind));
                            AddText(span.Text);
                            if (span.Kind.Length > 0)
                                Pop();
                        }
                        Pop();
                        Pop();
                        break;
                    case ListBlock list:
                        var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                        foreach (var item in list)
                        {
                            PushIndent(depth + 1);
                            AddText(list.IsOrdered ? $"{number++}. " : "• ");
                            RenderBlocks((ContainerBlock)item, depth + 1);
                            Pop();
                        }
                        break;
                    case QuoteBlock quote:
                        PushIndent(1);
                        PushColor(UiTokens.Muted);
                        RenderBlocks(quote, depth + 1);
                        Pop();
                        Pop();
                        break;
                    case ThematicBreakBlock:
                        AddText("────────────────────────");
                        break;
                    case ContainerBlock container:
                        RenderBlocks(container, depth + 1);
                        break;
                }
                AddText("\n\n");
            }
        }

        private void RenderInlines(ContainerInline container, int depth)
        {
            if (container == null || depth > 32)
                return;
            foreach (var inline in container)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        AddText(literal.Content.ToString());
                        break;
                    case CodeInline code:
                        PushMono();
                        AddText(code.Content);
                        Pop();
                        break;
                    case LineBreakInline line:
                        AddText(line.IsHard ? "\n" : " ");
                        break;
                    case EmphasisInline emphasis:
                        if (emphasis.DelimiterCount >= 2)
                            PushBold();
                        else
                            PushItalics();
                        RenderInlines(emphasis, depth + 1);
                        Pop();
                        break;
                    case LinkInline image when image.IsImage:
                        RenderImage(image);
                        break;
                    case LinkInline link:
                        var allowed = IsWebLink(link.Url);
                        if (allowed)
                        {
                            PushColor(UiTokens.TypeTarget);
                            PushMeta(link.Url);
                        }
                        RenderInlines(link, depth + 1);
                        if (allowed)
                        {
                            Pop();
                            Pop();
                        }
                        break;
                    case AutolinkInline autoLink:
                        if (IsWebLink(autoLink.Url))
                        {
                            PushMeta(autoLink.Url);
                            AddText(autoLink.Url);
                            Pop();
                        }
                        else
                            AddText(autoLink.Url);
                        break;
                    case ContainerInline nested:
                        RenderInlines(nested, depth + 1);
                        break;
                }
            }
        }

        private void RenderImage(LinkInline image)
        {
            var alt = image.FirstChild is LiteralInline literal ? literal.Content.ToString() : image.Title ?? "";
            var path = ModDocumentation.ResolveResource(document.Path, image.Url);
            var texture = path == null ? null : LoadImage(path);
            if (texture == null)
            {
                AddText($"[{alt}] ({TranslationServer.Translate("mod_manager/docs_image_unavailable")})");
                return;
            }
            AddImage(texture, width: 100, height: 0, widthInPercent: true, altText: alt, tooltip: image.Title ?? alt);
        }

        private ImageTexture LoadImage(string path)
        {
            if (textures.TryGetValue(path, out var cached))
                return cached;
            // Do not allow a document to retain an unbounded number of textures.
            if (textures.Count >= 16)
                return null;
            var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".webp")
                return null;
            var bytes = ModDocumentation.ReadResource(provider, path, 8 * 1024 * 1024);
            if (bytes == null || bytes.Length == 0)
                return null;
            using var image = new Image();
            var error = extension == ".png" ? image.LoadPngFromBuffer(bytes)
                : extension == ".webp" ? image.LoadWebpFromBuffer(bytes) : image.LoadJpgFromBuffer(bytes);
            if (error != Error.Ok || image.GetWidth() > 4096 || image.GetHeight() > 4096)
                return null;
            var texture = ImageTexture.CreateFromImage(image);
            textures[path] = texture;
            return texture;
        }

        private static bool IsWebLink(string value)
            => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http");

        // Same semantic palette as site/css/highlight-theme.css, independent of
        // the application's theme so examples remain recognisable on both UIs.
        private static Color CodeColor(string kind) => kind switch
        {
            "keyword" => new Color("ff7b72"),
            "function" or "type" => new Color("d2a8ff"),
            "string" => new Color("a5d6ff"),
            "comment" => new Color("8b949e"),
            "built_in" => new Color("ffa657"),
            "label" => new Color("1f6feb"),
            "directory" => new Color("e3b341"),
            "treecomment" => new Color("3fb950"),
            _ => new Color("79c0ff"),
        };

        private void OnLinkClicked(Variant value)
        {
            var url = value.AsString();
            if (IsWebLink(url))
                OS.ShellOpen(url);
        }
    }
}
