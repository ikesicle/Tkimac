using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Markdig;
using UnityEngine.UIElements;
using Markdig.Syntax;
using System.Linq;
using Markdig.Helpers;
using Markdig.Syntax.Inlines;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;

namespace ThunderKit.Markdown
{
    public enum MarkdownDataType { Source, Text }
    public class MarkdownElement : VisualElement
    {
        private static Regex LiteralSplitter = new Regex("^([\\S]+\\b\\S?)|^\\s+", RegexOptions.Singleline | RegexOptions.Compiled);
        private static event EventHandler UpdateMarkdown;
        static MarkdownElement()
        {
            EditorApplication.projectChanged += EditorApplication_projectChanged;
        }
        private static void EditorApplication_projectChanged()
        {
            UpdateMarkdown?.Invoke(null, EventArgs.Empty);
        }
        public string Data { get; set; }
        public MarkdownDataType MarkdownDataType { get; set; }
        public MarkdownElement()
        {
            //style.flexDirection = FlexDirection.Row;
            //style.flexWrap = Wrap.Wrap;
        }

        public new class UxmlFactory : UxmlFactory<MarkdownElement, UxmlTraits> { }
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private UxmlStringAttributeDescription m_text = new UxmlStringAttributeDescription { name = "data" };
            private UxmlEnumAttributeDescription<MarkdownDataType> m_dataType = new UxmlEnumAttributeDescription<MarkdownDataType> { name = "markdown-data-type" };
            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var mdElement = (MarkdownElement)ve;
                mdElement.Clear();
                mdElement.Data = m_text.GetValueFromBag(bag, cc);
                mdElement.MarkdownDataType = m_dataType.GetValueFromBag(bag, cc);

                var markdown = string.Empty;
                switch (mdElement.MarkdownDataType)
                {
                    case MarkdownDataType.Source:
                        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(mdElement.Data);
                        markdown = asset?.text ?? string.Empty;
                        break;
                    case MarkdownDataType.Text:
                        markdown = mdElement.Data;
                        break;
                }
                if (markdown == string.Empty)
                {
                    mdElement.Add(new Label($"No data found: {mdElement.MarkdownDataType} : {mdElement.Data}"));
                    return;
                }
                var document = Markdig.Markdown.Parse(markdown);
                foreach (Block block in document)
                {
                    var child = ProcessBlock(block);
                    if (child != null)
                        mdElement.Add(child);
                }
            }

            private static VisualElement ProcessBlock(Block block)
            {
                var blockElement = GetClassedElement<VisualElement>();
                if (block is LeafBlock lb && lb.Inline != null)
                    foreach (var child in ProcessInline(lb.Inline))
                        blockElement.Add(child);

                switch (block)
                {
                    case CodeBlock c:
                        blockElement.AddToClassList("code");
                        blockElement.name = "code";
                        break;
                    case ParagraphBlock p:
                        blockElement.name = "paragraph";
                        blockElement.AddToClassList("paragraph");
                        break;
                    case HeadingBlock h:
                        blockElement.name = $"heading-{h.Level}";
                        blockElement.AddToClassList($"header-{h.Level}");
                        break;
                    case ListItemBlock li:
                        var listBlock = li.Parent as ListBlock;
                        blockElement.name = $"list-item-{li.Order}";
                        var firstElement = true;
                        foreach (var inner in li)
                        {
                            var child = ProcessBlock(inner);
                            if (child != null)
                            {
                                if (firstElement)
                                {
                                    firstElement = false;
                                    var inline = child.Q<Label>(className: "inline");
                                    switch (listBlock.BulletType)
                                    {
                                        case '*':
                                            inline.text = $"* {inline.text}";
                                            break;
                                        case '1':
                                            inline.text = $"{li.Order}. {inline.text}";
                                            break;
                                        case '-':
                                            inline.text = $"- {inline.text}";
                                            break;
                                    }
                                }
                                blockElement.Add(child);
                            }
                        }

                        blockElement.AddToClassList("list-item");
                        break;
                    case ListBlock l:
                        foreach (var inner in l)
                        {
                            var child = ProcessBlock(inner);
                            if (child != null)
                                blockElement.Add(child);
                        }
                        blockElement.name = "list";
                        blockElement.AddToClassList("list");
                        break;
                    case QuoteBlock q:
                        blockElement.name = "quote";
                        blockElement.AddToClassList("quote");
                        break;
                    default:
                        break;
                }

                return blockElement;
            }

            private static IEnumerable<VisualElement> ProcessInline(Inline inline)
            {
                switch (inline)
                {
                    #region hiding stuff
                    case LinkInline lb:
                        {
                            if (lb.IsImage)
                            {
                                var imageElement = GetClassedElement<Image>("image");

                                async Task DownloadImage(string MediaUrl)
                                {
                                    UnityWebRequest request = UnityWebRequestTexture.GetTexture(MediaUrl);
                                    var asyncOp = request.SendWebRequest();
                                    while (!asyncOp.isDone)
                                        await Task.Delay(100);

                                    if (request.isNetworkError || request.isHttpError)
                                        Debug.Log(request.error);
                                    else
                                    {
                                        imageElement.image = ((DownloadHandlerTexture)request.downloadHandler).texture;
                                        imageElement.style.width = imageElement.image.width;
                                        imageElement.style.height = imageElement.image.height;
                                    }
                                }
                                _ = DownloadImage(lb.Url);
                                yield return imageElement;
                                break;
                            }
                            else
                            {
                                var firstChild = lb.FirstChild as LiteralInline;
                                var label = GetTextElement<Label>(firstChild.Content.ToString(), "link");
                                var url = lb.Url;
                                label.tooltip = url;
                                label.displayTooltipWhenElided = true;
                                if (url.StartsWith("Packages") || url.StartsWith("Assets"))
                                {
                                    if (Path.GetExtension(url).Equals(".md"))
                                    {

                                    }
                                    else
                                    {
                                        label.AddToClassList("asset-link");
                                        label.RegisterCallback<MouseUpEvent>(evt =>
                                        {
                                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(url);
                                            EditorGUIUtility.PingObject(asset);
                                            Selection.activeObject = asset;
                                        });
                                    }
                                }
                                else
                                {
                                    label.RegisterCallback<MouseUpEvent>(evt =>
                                    {
                                        var uri = new Uri(url);
                                        switch (uri.Scheme)
                                        {
                                            case "https":
                                            case "http":
                                                System.Diagnostics.Process.Start(url);
                                                break;
                                        }
                                    });
                                }

                                yield return label;
                                break;
                            }
                        }
                    case AutolinkInline ali:
                        var autoLink = GetTextElement<Label>(ali.Url, "autolink");
                        yield return autoLink;
                        break;
                    case CodeInline ci:
                        yield return GetTextElement<Label>(ci.Content, "code");
                        break;
                    case EmphasisDelimiterInline edi:
                        //yield return GetTextElement<Label>(text.Substring(edi.Span.Start, edi.Span.Length), "emphasis", "delimiter");
                        break;
                    case LinkDelimiterInline lb:
                        //yield return GetTextElement<Label>(text.Substring(lb.Span.Start, lb.Span.Length), "link", "delimiter");
                        break;
                    case DelimiterInline ci:

                        //yield return GetTextElement<Label>(text.Substring(ci.Span.Start, ci.Span.Length), "delimiter");
                        break;
                    case EmphasisInline ei:
                        foreach (var inner in ei)
                        {
                            foreach (var element in ProcessInline(inner))
                            {
                                if (ei.IsDouble)
                                {
                                    if (element.ClassListContains("italic"))
                                    {
                                        element.RemoveFromClassList("italic");
                                        element.AddToClassList("emphasis");
                                    }
                                    else element.AddToClassList("bold");
                                }
                                else
                                {
                                    if (element.ClassListContains("bold"))
                                    {
                                        element.RemoveFromClassList("bold");
                                        element.AddToClassList("emphasis");
                                    }
                                    else element.AddToClassList("italic");
                                }
                                yield return element;
                            }
                        }
                        break;
                    case ContainerInline container:
                        {
                            foreach (var childInline in container)
                                foreach (var child in ProcessInline(childInline))
                                    if (child != null)
                                        yield return child;
                        }
                        break;
                    case LineBreakInline lbi:
                        yield return GetClassedElement<VisualElement>("linebreak");
                        break;
                    #endregion
                    case LiteralInline i:
                        var content = i.Content.ToString();
                        int safetyBreak = 0;
                        while (++safetyBreak < 10 && !string.IsNullOrWhiteSpace(content) && content.Length > 0)
                        {
                            var match = LiteralSplitter.Match(content);
                            if (match.Success)
                            {
                                if (!string.IsNullOrEmpty(match.Value) && !string.IsNullOrWhiteSpace(match.Value))
                                {
                                    safetyBreak = 0;
                                    content = content.Substring(match.Value.Length);
                                    yield return GetTextElement<Label>(match.Value, "inline");
                                }
                                else
                                    content = content.Substring(1);
                            }
                            else
                                break;
                        }
                        break;
                    default:
                        yield return null;
                        break;
                }
            }

            private static T GetTextElement<T>(string text, params string[] classNames) where T : TextElement, new()
            {
                T element = GetClassedElement<T>(classNames);

                element.text = text;

                return element;
            }

            private static T GetClassedElement<T>(params string[] classNames) where T : VisualElement, new()
            {
                T element = new T();

                element.style.whiteSpace = WhiteSpace.Normal;

                if (classNames == null || classNames.Length == 0) return element;
                element.name = classNames[0];

                for (int i = 0; i < classNames.Length; i++)
                    element.AddToClassList(classNames[i]);

                return element;
            }

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get
                {
                    yield break;
                }
            }
        }
    }
}