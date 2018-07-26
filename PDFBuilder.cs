using iTextSharp.text;
using iTextSharp.text.pdf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace iText5PDFBuilder
{
    public class PDFBuilderCore
    {
        private class Html2PdfContext
        {
            [Flags]
            public enum ChunkFontStyle
            {
                None = 0,
                Strong = Font.BOLD,
                Emphasis = Font.ITALIC,
            }

            public enum ParaStyle
            {
                Body,
                Header,
                HeaderMinor,
            }

            public ChunkFontStyle defaultFontStyle = ChunkFontStyle.None;
            public float defaultFontSize = 8.0f;
            public int defaultAlignment = Element.ALIGN_LEFT;

            public Font bodyFont;
            public Font bodyFontHeader;
            public Font bodyFontHeaderMinor;

            public Paragraph currentParagraph = null;

            public List currentList = null;
            public ListItem currentListItem = null;

            public Anchor currentAnchor = null;

            public Chunk currentChunk = null;

            public Stack<ChunkFontStyle> chunkFontStyles = new Stack<ChunkFontStyle>();
            public Stack<float> chunkFontSizes = new Stack<float>();
            public bool hasListItems = false;

            public void PushChunkFontStyle(ChunkFontStyle style)
            {
                chunkFontStyles.Push(style);
            }

            public ChunkFontStyle CurrentChunkFontStyle
            {
                get
                {
                    if (chunkFontStyles.Count == 0)
                    {
                        return defaultFontStyle;
                    }

                    return chunkFontStyles.Peek();
                }
            }

            public void PopChunkFontStyle()
            {
                if (chunkFontStyles.Count > 0)
                {
                    chunkFontStyles.Pop();
                }
            }

            public void PushChunkFontSize(float size)
            {
                chunkFontSizes.Push(size);
            }

            public float CurrentChunkFontSize
            {
                get
                {
                    if (chunkFontSizes.Count == 0)
                    {
                        return defaultFontSize;
                    }

                    return chunkFontSizes.Peek();
                }
            }

            public void PopChunkFontSize()
            {
                if (chunkFontSizes.Count > 0)
                {
                    chunkFontSizes.Pop();
                }
            }

            private Chunk CreateChunk(ChunkFontStyle style)
            {
                currentChunk = new Chunk();
                currentChunk.Font = new Font(bodyFont.BaseFont, CurrentChunkFontSize, (int)CurrentChunkFontStyle);
                return currentChunk;
            }

            public Paragraph CreateParagraph(ParaStyle style, List<IElement> elements)
            {
                currentParagraph = new Paragraph();
                currentParagraph.Alignment = defaultAlignment;

                switch (style)
                {
                    case ParaStyle.Body:
                        currentParagraph.Font = bodyFont;
                        currentParagraph.SpacingBefore = 0;
                        break;
                    case ParaStyle.Header:
                        currentParagraph.Font = bodyFontHeader;
                        currentParagraph.SpacingBefore = 20;
                        break;
                    case ParaStyle.HeaderMinor:
                        currentParagraph.Font = bodyFontHeaderMinor;
                        currentParagraph.SpacingBefore = 10;
                        break;
                }

                currentParagraph.Alignment = Element.ALIGN_LEFT;

                if (elements != null)
                {
                    elements.Add(currentParagraph);
                }

                return currentParagraph;
            }

            public void EatText(string cleanPara, List<IElement> elements)
            {
                if (string.IsNullOrEmpty(cleanPara))
                {
                    return;
                }

                CreateChunk(CurrentChunkFontStyle);
                currentChunk.Append(cleanPara);

                if (currentAnchor != null)
                {
                    currentAnchor.Add(currentChunk);
                }
                else if (currentListItem != null)
                {
                    currentListItem.Add(currentChunk);
                }
                else if (currentParagraph != null)
                {
                    currentParagraph.Add(currentChunk);
                }
                else
                {
                    CreateParagraph(ParaStyle.Body, elements);
                    currentParagraph.Add(currentChunk);
                }
            }
        }

        static readonly Regex _findHtmlToken = new Regex("(</|<)\\s*(\\w+)[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex _findHrefLink = new Regex("href=\"([^\"]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly HashSet<string> _htmlBlockTags = new HashSet<string>() { "p", "h1", "h2", "h3", "h4", "h5", "h6", "ol", "ul", "pre", "address", "blockquote", "dl", "div" };
        static readonly HashSet<string> _htmlLineBreakTags = new HashSet<string>() { "br" };
        static readonly HashSet<string> _htmlNoCloseTags = new HashSet<string>() { "br", "img" };

        private static bool IsBlockElementTag(string tagLower)
        {
            return _htmlBlockTags.Contains(tagLower);
        }

        private static bool IsLineBreakingTag(string tagLower)
        {
            return _htmlLineBreakTags.Contains(tagLower);
        }

        private static string CleanupParagraph(string paraText)
        {
            paraText = paraText.Replace("\r\n", " ");
            paraText = paraText.Replace("\r", " ");
            paraText = paraText.Replace("\n", " ");

            for (;;)
            {
                string orig = paraText;
                paraText = paraText.Replace("  ", " ");
                if (orig == paraText)
                {
                    break;
                }
            }

            return paraText;
        }

        private static void EncryptPdf(Stream input, Stream output)
        {
            using (PdfReader reader = new PdfReader(input))
            using (PdfStamper stamper = new PdfStamper(reader, output))
            {
                stamper.SetEncryption(null, Encoding.UTF8.GetBytes("vsdffvgfsdvwvw"),
                                      PdfWriter.ALLOW_PRINTING,
                                      PdfWriter.ENCRYPTION_AES_256 | PdfWriter.DO_NOT_ENCRYPT_METADATA);
            }
        }

        private static List<IElement> ParseHTML(string rawHtml, Html2PdfContext ctx)
        {
            List<IElement> elements = new List<IElement>();

            MatchCollection matches = _findHtmlToken.Matches(rawHtml);

            StringBuilder sb = new StringBuilder();
            int lastIndex = 0;

            foreach (Match match in matches)
            {
                int textToEatLen = match.Index - lastIndex;
                if (textToEatLen > 0)
                {
                    string textToEat = rawHtml.Substring(lastIndex, textToEatLen);
                    sb.Append(textToEat);
                }

                string cleanPara = CleanupParagraph(sb.ToString());

                bool isOpeningTag = true;
                if (match.Groups[1].Value == "</")
                {
                    isOpeningTag = false;
                }

                string tag = match.Groups[2].Value.ToLower();

                if (isOpeningTag)
                {
                    ctx.EatText(cleanPara, elements);
                    sb.Clear();

                    if (tag == "strong")
                    {
                        ctx.PushChunkFontStyle(Html2PdfContext.ChunkFontStyle.Strong);
                    }
                    else if (tag == "em")
                    {
                        ctx.PushChunkFontStyle(Html2PdfContext.ChunkFontStyle.Emphasis);
                    }
                    else if (tag == "a")
                    {
                        Match hrefMatch = _findHrefLink.Match(match.Value);
                        if (hrefMatch.Success)
                        {
                            string url = hrefMatch.Groups[1].Value;

                            ctx.currentAnchor = new Anchor();
                            ctx.currentAnchor.Font = new Font(ctx.bodyFont.BaseFont, ctx.CurrentChunkFontSize, Font.UNDERLINE, ctx.bodyFont.Color);
                            ctx.currentAnchor.Reference = url;

                            if (ctx.currentListItem != null)
                            {
                                ctx.currentListItem.Add(ctx.currentAnchor);
                            }
                            else if (ctx.currentParagraph != null)
                            {
                                ctx.currentParagraph.Add(ctx.currentAnchor);
                            }
                        }
                    }
                    else if (tag == "ul")
                    {
                        List list = new List(false, false, ctx.bodyFont.Size * 1.5f);
                        list.ListSymbol = new Chunk("");
                        elements.Add(list);
                        ctx.currentList = list;
                        ctx.hasListItems = false;
                    }
                    else if (tag == "ol")
                    {
                        List list = new List(true, false, ctx.bodyFont.Size * 1.5f);
                        elements.Add(list);
                        ctx.currentList = list;
                        ctx.hasListItems = false;
                    }
                    else if (tag == "li")
                    {
                        if (ctx.currentList == null)
                        {
                            List list = new List(false, false);
                            elements.Add(list);
                            ctx.currentList = list;
                        }

                        ListItem li = new ListItem();
                        li.SpacingBefore = 4;
                        li.Font = ctx.bodyFont;
                        ctx.currentList.Add(li);
                        ctx.currentListItem = li;
                        ctx.hasListItems = true;
                    }
                    else if (tag == "h1" || tag == "h2" || tag == "h3")
                    {
                        ctx.CreateParagraph(Html2PdfContext.ParaStyle.Header, elements);
                        ctx.PushChunkFontSize(ctx.bodyFontHeader.Size);
                    }
                    else if (tag == "h4" || tag == "h5" || tag == "h6")
                    {
                        ctx.CreateParagraph(Html2PdfContext.ParaStyle.HeaderMinor, elements);
                        ctx.PushChunkFontSize(ctx.bodyFontHeaderMinor.Size);
                    }
                    else if (IsBlockElementTag(tag))
                    {
                        ctx.CreateParagraph(Html2PdfContext.ParaStyle.Body, elements);
                        ctx.PushChunkFontSize(ctx.bodyFont.Size);
                    }
                    else if (IsLineBreakingTag(tag))
                    {
                        sb.Append("\n");
                    }
                }
                else
                {
                    ctx.EatText(cleanPara, elements);
                    sb.Clear();

                    if (tag == "strong" || tag == "em")
                    {
                        ctx.PopChunkFontStyle();
                    }
                    else if (tag == "h1" || tag == "h2" || tag == "h3" || tag == "h4" || tag == "h5" || tag == "h6" || IsBlockElementTag(tag))
                    {
                        ctx.PopChunkFontSize();
                    }

                    if (tag == "li")
                    {
                        ctx.currentListItem = null;
                    }
                    else if (tag == "ol" || tag == "ul")
                    {
                        ctx.currentListItem = null;
                        ctx.currentList = null;

                        if (ctx.hasListItems)
                        {
                            //
                            // todo: Extra spacing after a list?
                            //
                            // elements.Add(new Paragraph(""));

                            ctx.hasListItems = false;
                        }
                    }
                    else if (tag == "a")
                    {
                        ctx.currentAnchor = null;
                    }
                }

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < rawHtml.Length)
            {
                string textToEat = rawHtml.Substring(lastIndex);
                textToEat = CleanupParagraph(textToEat);
                ctx.EatText(textToEat, elements);
            }

            List<IElement> elementsToRemove = new List<IElement>();
            foreach (IElement element in elements)
            {
                if (element is Paragraph)
                {
                    Paragraph p = (Paragraph)element;
                    if (p.Count >= 1 && p.Content.Trim().Length == 0)
                    {
                        elementsToRemove.Add(p);
                    }
                }
            }

            foreach (IElement elementToRemove in elementsToRemove)
            {
                elements.Remove(elementToRemove);
            }

            return elements;
        }


        public static bool CreatePdf(Stream output, Config config)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (Document document = new Document(PageSize.LETTER))
                using (PdfWriter writer = PdfWriter.GetInstance(document, ms))
                {
                    document.AddTitle($"{config.website} - {config.title}");
                    document.AddAuthor($"{config.website}");
                    document.AddSubject(config.title);
                    document.AddKeywords($"{config.keywords}");
                    document.AddCreator($"{config.creator}");
                    document.AddCreationDate();
                    document.AddLanguage("en");

                    document.Open();

                    document.SetPageSize(PageSize.LETTER);

                    float w = document.PageSize.Width - document.LeftMargin - document.RightMargin;
                    float h = document.PageSize.Height - document.TopMargin - document.BottomMargin;

                    float headerInfoHeight = 68;

                    float headerHeight = h / 16;
                    float headerImageHeight = headerHeight + document.TopMargin + headerInfoHeight;
                    float headerImageWidth = (headerImageHeight * 640.0f) / 480.0f;
                    float headerWidth = w - headerImageWidth;
                    float headerTitleHeight = headerImageHeight - headerInfoHeight;

                    Rectangle[] bodyLayoutRects = new Rectangle[]
                    {
                        //
                        // Page 1
                        //
                        new Rectangle(0,0,0,0),
                        new Rectangle(0,0,0,0),
                        //
                        // Page 2+
                        //
                        new Rectangle(0,0,0,0),
                        new Rectangle(0,0,0,0),

                        //
                        // Footer
                        //
                        new Rectangle(0,0,0,0),
                    };

                    Font fontTitle = new Font(Font.FontFamily.HELVETICA, 16, Font.BOLD, BaseColor.DARK_GRAY);
                    Font fontPrimaryInfo = new Font(Font.FontFamily.HELVETICA, 8, Font.BOLD, BaseColor.DARK_GRAY);
                    Font fontSecondaryInfo = new Font(Font.FontFamily.HELVETICA, 8, Font.NORMAL, BaseColor.DARK_GRAY);
                    Font fontFooter = new Font(Font.FontFamily.HELVETICA, 6, Font.NORMAL, BaseColor.GRAY);

                    Font fontBodyNormal = new Font(Font.FontFamily.HELVETICA, 8, Font.NORMAL, BaseColor.BLACK);
                    Font fontBodyHeader = new Font(Font.FontFamily.HELVETICA, 12, Font.BOLD, BaseColor.DARK_GRAY);
                    Font fontBodyHeaderMinor = new Font(Font.FontFamily.HELVETICA, 10, Font.BOLD, BaseColor.DARK_GRAY);

                    bodyLayoutRects[2].Left = bodyLayoutRects[0].Left = document.LeftMargin;
                    bodyLayoutRects[1].Top = bodyLayoutRects[0].Top = document.TopMargin;
                    bodyLayoutRects[2].Right = bodyLayoutRects[0].Right = bodyLayoutRects[0].Left + ((w / 2) - (document.LeftMargin / 2));
                    bodyLayoutRects[1].Bottom = bodyLayoutRects[0].Bottom = bodyLayoutRects[0].Top + (h - (headerImageHeight - document.TopMargin / 2.0f));

                    bodyLayoutRects[3].Left = bodyLayoutRects[1].Left = document.LeftMargin + (w / 2) + (document.LeftMargin / 2);
                    bodyLayoutRects[3].Right = bodyLayoutRects[1].Right = bodyLayoutRects[1].Left + ((w / 2) - (document.LeftMargin));

                    bodyLayoutRects[3].Top = bodyLayoutRects[2].Top = document.TopMargin;
                    bodyLayoutRects[3].Bottom = bodyLayoutRects[2].Bottom = bodyLayoutRects[2].Top + (h - (document.TopMargin / 2.0f));

                    bodyLayoutRects[4].Left = document.LeftMargin;
                    bodyLayoutRects[4].Right = w;
                    bodyLayoutRects[4].Top = 0;
                    bodyLayoutRects[4].Bottom = document.TopMargin / 2;

                    Rectangle[] headerLayoutRects = new Rectangle[]
                    {
                        //
                        // Title
                        //
                        new Rectangle(0,0,0,0),
                        //
                        // Info
                        //
                        new Rectangle(0,0,0,0),
                    };


                    headerLayoutRects[0].Left = document.LeftMargin;
                    headerLayoutRects[0].Top = document.PageSize.Height - (headerTitleHeight + document.TopMargin / 2);
                    headerLayoutRects[0].Right = headerLayoutRects[0].Left + headerWidth;
                    headerLayoutRects[0].Bottom = headerLayoutRects[0].Top + headerTitleHeight;

                    headerLayoutRects[1].Left = document.LeftMargin;
                    headerLayoutRects[1].Top = document.PageSize.Height - headerImageHeight + 4;
                    headerLayoutRects[1].Right = headerLayoutRects[1].Left + headerWidth;
                    headerLayoutRects[1].Bottom = headerLayoutRects[1].Top + headerInfoHeight;

                    //
                    // Cover Image
                    //
                    System.Drawing.Image sysCoverImage = CreateCoverImage(config.imageUrl, config.overlayUrl, headerImageWidth * 4, headerImageHeight * 4);
                    if (sysCoverImage != null)
                    {
                        iTextSharp.text.Image coverImage = iTextSharp.text.Image.GetInstance(sysCoverImage, System.Drawing.Imaging.ImageFormat.Jpeg);
                        if (coverImage != null)
                        {
                            coverImage.SetAbsolutePosition(document.PageSize.Width - headerImageWidth, document.PageSize.Height - headerImageHeight);
                            coverImage.ScaleToFit(headerImageWidth, headerImageHeight);

                            document.Add(coverImage);
                        }
                    }

                    ColumnText ct = new ColumnText(writer.DirectContent);

                    //
                    // Title
                    //
                    {
                        Paragraph p = new Paragraph();
                        p.Alignment = Element.ALIGN_CENTER;
                        p.Font = fontTitle;

                        p.Add(config.title);

                        ct.AddElement(p);

                        p = new Paragraph();
                        p.Alignment = Element.ALIGN_CENTER;
                        p.Font = fontPrimaryInfo;

                        p.Add(config.subHeader1);

                        ct.AddElement(p);

                        ct.SetSimpleColumn(headerLayoutRects[0]);
                        ct.Go();
                    }

                    //
                    // Info
                    //
                    {
                        Paragraph p = new Paragraph();
                        p.Alignment = Element.ALIGN_CENTER;
                        p.Font = fontSecondaryInfo;

                        Html2PdfContext ctxSubHeader = new Html2PdfContext()
                        {
                            defaultFontSize = fontSecondaryInfo.Size,
                            defaultAlignment = Element.ALIGN_CENTER,

                            bodyFont = fontSecondaryInfo,
                            bodyFontHeader = fontSecondaryInfo,
                            bodyFontHeaderMinor = fontSecondaryInfo,
                        };

                        List<IElement> elementsNutrition = ParseHTML(config.subHeader2, ctxSubHeader);
                        foreach (IElement el in elementsNutrition)
                        {
                            if (el is Paragraph)
                            {
                                ((Paragraph)el).Alignment = Element.ALIGN_CENTER;
                            }
                            ct.AddElement(el);
                        }

                        ct.SetSimpleColumn(headerLayoutRects[1]);
                        ct.Alignment = Element.ALIGN_CENTER;
                        ct.Go();
                    }

                    Html2PdfContext ctx = new Html2PdfContext()
                    {
                        defaultFontSize = fontBodyNormal.Size,

                        bodyFont = fontBodyNormal,
                        bodyFontHeader = fontBodyHeader,
                        bodyFontHeaderMinor = fontBodyHeaderMinor,
                    };

                    List<IElement> elements = ParseHTML(config.body, ctx);
                    foreach (IElement el in elements)
                    {
                        ct.AddElement(el);
                    }

                    int c = 0;
                    int status = 0;
                    while (ColumnText.HasMoreText(status))
                    {
                        ct.SetSimpleColumn(bodyLayoutRects[c]);
                        status = ct.Go();
                        ++c;
                        if (c == 2)
                        {
                            ColumnText.ShowTextAligned(writer.DirectContentUnder, Element.ALIGN_CENTER, new Phrase(config.footer, fontFooter),
                                    (bodyLayoutRects[4].Left + bodyLayoutRects[4].Right) / 2,
                                    bodyLayoutRects[4].Bottom - (fontFooter.Size / 2),
                                    0);

                            document.NewPage();
                        }
                        else if (c == 4)
                        {
                            ColumnText.ShowTextAligned(writer.DirectContentUnder, Element.ALIGN_CENTER, new Phrase(config.footer, fontFooter),
                                    (bodyLayoutRects[4].Left + bodyLayoutRects[4].Right) / 2,
                                    bodyLayoutRects[4].Bottom - (fontFooter.Size / 2),
                                    0);

                            document.NewPage();
                            c = 2;
                        }
                    }

                    //
                    // needed to ensure we dont access the stream
                    //
                    document.Close();
                }

                MemoryStream msReadFrom = new MemoryStream(ms.GetBuffer());
                EncryptPdf(msReadFrom, output);
            }

            return true;
        }

        private static System.Drawing.Bitmap BitmapFromURL(string imageUrl)
        {
            using (WebClient wc = new WebClient())
            {
                using (Stream s = wc.OpenRead(imageUrl))
                {
                    return new System.Drawing.Bitmap(s);
                }
            }
        }

        private static float GetBitmapApectRatio(System.Drawing.Bitmap bmp)
        {
            return (bmp.Height == 0) ? 0 : ((float)bmp.Width / (float)bmp.Height);
        }

        private static System.Drawing.Image CreateCoverImage(string imageUrl, string overlayUrl, float width, float height)
        {
            try
            {
                using (System.Drawing.Bitmap src = BitmapFromURL(imageUrl))
                using (System.Drawing.Bitmap overlay = BitmapFromURL(overlayUrl))
                {
                    System.Drawing.Rectangle cropRect = new System.Drawing.Rectangle(0, 0, (int)Math.Round(width), (int)Math.Round(height));
                    System.Drawing.Bitmap target = new System.Drawing.Bitmap(cropRect.Width, cropRect.Height);

                    float leftSrcOffset = 0;
                    float topSrcOffset = 0;
                    float widthSrc = 0;
                    float heightSrc = 0;

                    if (GetBitmapApectRatio(src) > GetBitmapApectRatio(target))
                    {
                        //
                        // source is wider
                        //

                        heightSrc = src.Height;
                        topSrcOffset = 0;

                        widthSrc = (width * heightSrc) / height;
                        leftSrcOffset = (src.Width - widthSrc) / 2.0f;
                    }
                    else
                    {
                        //
                        // source is taller
                        //

                        widthSrc = src.Width;
                        leftSrcOffset = 0;

                        heightSrc = (height * widthSrc) / width;
                        topSrcOffset = (src.Width - heightSrc) / 2.0f;
                    }

                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(target))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                        g.DrawImage(src, cropRect, 
                                            new System.Drawing.RectangleF(leftSrcOffset, topSrcOffset, widthSrc, heightSrc),
                                            System.Drawing.GraphicsUnit.Pixel);

                        g.DrawImage(overlay, cropRect,
                                            new System.Drawing.RectangleF(0, 0, overlay.Width, overlay.Height),
                                            System.Drawing.GraphicsUnit.Pixel);
                    }

                    return target;
                }
            }
            catch
            {
            }

            return null;
        }
    }

}
