// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using CommunityToolkit.WinUI.Controls.MarkdownTextBlockRns;

namespace CommunityToolkit.Labs.WinUI.MarkdownTextBlock;

internal record MarkdownConfig
{
    public string? BaseUrl { get; set; }
    public IImageProvider? ImageProvider { get; set; }
    public ISVGRenderer? SVGRenderer { get; set; }
    public MarkdownThemes Themes { get; set; } = MarkdownThemes.Default;

#pragma warning disable SA1401 // Fields should be private
    public static MarkdownConfig Default = new();
#pragma warning restore SA1401 // Fields should be private
}