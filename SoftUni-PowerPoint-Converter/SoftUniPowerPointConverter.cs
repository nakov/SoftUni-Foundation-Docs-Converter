﻿using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Office.Interop.PowerPoint;
using Microsoft.Office.Core;
using SoftUniConverterCommon;
using static SoftUniConverterCommon.ConverterUtils;
using Shape = Microsoft.Office.Interop.PowerPoint.Shape;

public class SoftUniPowerPointConverter
{
    static readonly string pptTemplateFileName = Path.GetFullPath(Directory.GetCurrentDirectory() +
        @"\..\..\..\Document-Templates\License-Slide-SoftUni-Foundation.pptx");
    static readonly string pptSourceFileName = Path.GetFullPath(Directory.GetCurrentDirectory() +
        @"\..\..\..\Sample-Docs\test11.pptx");
    static readonly string pptDestFileName = Directory.GetCurrentDirectory() + @"\converted.pptx";

    static void Main()
    {
        ConvertAndFixPresentation(pptSourceFileName, pptDestFileName, pptTemplateFileName, true);
    }

    public static void ConvertAndFixPresentation(string pptSourceFileName, 
        string pptDestFileName, string pptTemplateFileName, bool appWindowVisible)
    {
        if (KillAllProcesses("POWERPNT"))
            Console.WriteLine("MS PowerPoint (POWERPNT.EXE) is still running -> process terminated.");

        MsoTriState pptAppWindowsVisible = appWindowVisible ?
            MsoTriState.msoTrue : MsoTriState.msoFalse;
        Application pptApp = new Application();
        try
        {
            Console.WriteLine("Processing input presentation: {0}", pptSourceFileName);
            Presentation pptSource = pptApp.Presentations.Open(
                pptSourceFileName, WithWindow: pptAppWindowsVisible);

            Console.WriteLine("Copying the PPTX template '{0}' as output presentation '{1}'",
                pptTemplateFileName, pptDestFileName);
            File.Copy(pptTemplateFileName, pptDestFileName, true);

            Console.WriteLine($"Opening the output presentation: {pptDestFileName}...");
            Presentation pptDestination = pptApp.Presentations.Open(
                pptDestFileName, WithWindow: pptAppWindowsVisible);

            List<string> pptTemplateSlideTitles = ExtractSlideTitles(pptDestination);

            RemoveAllSectionsAndSlides(pptDestination);

            CopyDocumentProperties(pptSource, pptDestination);

            CopySlidesAndSections(pptSource, pptDestination);

            FixCodeBoxes(pptSource, pptDestination);

            pptSource.Close();

            Language lang = DetectPresentationLanguage(pptDestination);

            FixLicenseSlide(pptDestination, pptTemplateFileName, pptTemplateSlideTitles, lang);

            FixInvalidSlideLayouts(pptDestination);

            FixSectionTitleSlides(pptDestination);

            FixSlideTitles(pptDestination);

            FixSlideNumbers(pptDestination);

            FixSlideNotesPages(pptDestination);

            pptDestination.Save();
            if (!appWindowVisible)
                pptDestination.Close();
        }
        finally
        {
            if (!appWindowVisible)
            {
                // Quit the MS Word application
                pptApp.Quit();

                // Release any associated .NET proxies for the COM objects, which are not in use
                // Intentionally we call the garbace collector twice
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }

    static List<Shape> ExtractSlideTitleShapes(Presentation presentation, bool includeSubtitles = false)
    {
        List<Shape> slideTitleShapes = new List<Shape>();
        foreach (Slide slide in presentation.Slides)
        {
            Shape slideTitleShape = null;
            foreach (Shape shape in slide.Shapes.Placeholders)
            {
                if (shape.Type == MsoShapeType.msoPlaceholder)
                    if (shape.PlaceholderFormat.Type == PpPlaceholderType.ppPlaceholderTitle)
                        if (shape.HasTextFrame == MsoTriState.msoTrue)
                            slideTitleShape = shape;
            }
            if (slideTitleShape == null)
            {
                if (slide.Shapes.Placeholders.Count > 0)
                {
                    Shape firstShape = slide.Shapes.Placeholders[1];
                    if (firstShape?.HasTextFrame == MsoTriState.msoTrue)
                        slideTitleShape = firstShape;
                }
            }
            slideTitleShapes.Add(slideTitleShape);

            if (includeSubtitles)
            {
                // Extract also subtitles
                foreach (Shape shape in slide.Shapes.Placeholders)
                {
                    if (shape.Type == MsoShapeType.msoPlaceholder)
                        if (shape.PlaceholderFormat.Type == PpPlaceholderType.ppPlaceholderSubtitle)
                            if (shape.HasTextFrame == MsoTriState.msoTrue)
                                slideTitleShapes.Add(shape);
                }
            }
        }
        return slideTitleShapes;
    }

    static List<string> ExtractSlideTitles(Presentation presentation)
    {
        List<Shape> slideTitleShapes = ExtractSlideTitleShapes(presentation);
        List<string> slideTitles = slideTitleShapes
            .Select(shape => shape?.TextFrame.TextRange.Text)
            .ToList();
        return slideTitles;
    }

    static void RemoveAllSectionsAndSlides(Presentation presentation)
    {
        Console.WriteLine("Removing all sections and slides from the template...");
        while (presentation.SectionProperties.Count > 0)
            presentation.SectionProperties.Delete(1, true);
        while (presentation.Slides.Count > 0)
            presentation.Slides[1].Delete();
    }

    static void CopySlidesAndSections(Presentation pptSource, Presentation pptDestination)
    {
        Console.WriteLine("Copying all slides and sections from the source presentation...");

        // Copy all slides from the source presentation
        Console.WriteLine("  Copying all slides from the source presentation...");
        pptDestination.Slides.InsertFromFile(pptSource.FullName, 0);

        // Copy all sections from the source presentation
        Console.WriteLine("  Copying all sections from the source presentation...");
        int sectionSlideIndex = 1;
        for (int sectNum = 1; sectNum <= pptSource.SectionProperties.Count; sectNum++)
        {
            string sectionName = pptSource.SectionProperties.Name(sectNum);
            sectionName = FixEnglishTitleCharacterCasing(sectionName);
            if (sectionSlideIndex <= pptDestination.Slides.Count)
                pptDestination.SectionProperties.AddBeforeSlide(sectionSlideIndex, sectionName);
            else
                pptDestination.SectionProperties.AddSection(sectNum, sectionName);
            sectionSlideIndex += pptSource.SectionProperties.SlidesCount(sectNum);
        }
    }

    static void FixCodeBoxes(Presentation pptSource, Presentation pptDestination)
    {
        Console.WriteLine("Fixing source code boxes...");

        int slidesCount = pptSource.Slides.Count;
        for (int slideNum = 1; slideNum <= slidesCount; slideNum++)
        {
            Slide newSlide = pptDestination.Slides[slideNum];
            if (newSlide.CustomLayout.Name == "Source Code Example")
            {
                Slide oldSlide = pptSource.Slides[slideNum];
                for (int shapeNum = 1; shapeNum <= newSlide.Shapes.Placeholders.Count; shapeNum++)
                {
                    Shape newShape = newSlide.Shapes.Placeholders[shapeNum];
                    if (newShape.HasTextFrame == MsoTriState.msoTrue &&
                        newShape.TextFrame.HasText == MsoTriState.msoTrue)
                    {
                        // Found [Code Box] -> copy the paragraph formatting from the original shape
                        Shape oldShape = oldSlide.Shapes.Placeholders[shapeNum];
                        newShape.TextFrame.TextRange.ParagraphFormat.SpaceBefore =
                            Math.Max(0, oldShape.TextFrame.TextRange.ParagraphFormat.SpaceBefore);
                        newShape.TextFrame.TextRange.ParagraphFormat.SpaceAfter =
                            Math.Max(0, oldShape.TextFrame.TextRange.ParagraphFormat.SpaceAfter);
                        newShape.TextFrame.TextRange.ParagraphFormat.SpaceWithin =
                            Math.Max(0, oldShape.TextFrame.TextRange.ParagraphFormat.SpaceWithin);
                        newShape.TextFrame.TextRange.LanguageID =
                            MsoLanguageID.msoLanguageIDEnglishUS;
                        // newShape.TextFrame.TextRange.NoProofing = MsoTriState.msoTrue;
                    }
                }

                Console.WriteLine($"  Fixed the code box styling at slide #{slideNum}");
            }
        }
    }

    static void CopyDocumentProperties(Presentation pptSource, Presentation pptDestination)
    {
        Console.WriteLine("Copying document properties (metadata)...");

        object srcDocProperties = pptSource.BuiltInDocumentProperties;
        string title = GetObjectProperty(srcDocProperties, "Title")
            ?.ToString()?.Replace(',', ';');
        string subject = GetObjectProperty(srcDocProperties, "Subject")
            ?.ToString()?.Replace(',', ';');
        string category = GetObjectProperty(srcDocProperties, "Category")
            ?.ToString()?.Replace(',', ';');
        string keywords = GetObjectProperty(srcDocProperties, "Keywords")
            ?.ToString()?.Replace(',', ';');

        object destDocProperties = pptDestination.BuiltInDocumentProperties;
        if (!string.IsNullOrWhiteSpace(title))
            SetObjectProperty(destDocProperties, "Title", title);
        if (!string.IsNullOrWhiteSpace(subject))
            SetObjectProperty(destDocProperties, "Subject", subject);
        if (!string.IsNullOrWhiteSpace(category))
            SetObjectProperty(destDocProperties, "Category", category);
        if (!string.IsNullOrWhiteSpace(keywords))
            SetObjectProperty(destDocProperties, "Keywords", keywords);
    }

    static void FixInvalidSlideLayouts(Presentation presentation)
    {
        Console.WriteLine("Fixing the invalid slide layouts...");

        var layoutMappings = new Dictionary<string, string> {
            { "Presentation Title Slide", "Presentation Title Slide" },
            { "Presentation Title", "Presentation Title Slide" },

            { "1_Presentation Title Slide", "1_Presentation Title Slide" },

            { "Title Slide", "Title Slide" },
            { "Title slide", "Title Slide" },
            { "Section Title Slide", "Title Slide" },
            { "Section Slide", "Title Slide" },
            { "Заглавен слайд", "Title Slide" },
            { "1_Title Slide", "Title Slide" },
            { "2_Title Slide", "Title Slide" },
            { "Title Only", "Title Slide" },
            { "Section Header", "Title Slide" },
            { "Picture with Caption", "Title Slide" },

            { "Title and Content", "Title and Content" },
            { "1_Title and Content", "Title and Content" },
            { "2_Title and Content", "Title and Content" },
            { "3_Title and Content", "Title and Content" },
            { "4_Title and Content", "Title and Content" },
            { "Заглавие и съдържание", "Title and Content" },
            { "Title, Content", "Title and Content" },
            { "Title, 2 Content", "Title and Content" },
            { "Title and body", "Title and Content" },
            { "Content with Caption", "Title and Content" },

            { "Blank Slide", "Blank Slide" },
            { "1_Blank Slide", "Blank Slide" },

            { "Questions Slide", "Questions Slide" },
            { "Слайд с въпроси", "Questions Slide" },
        };
        const string defaultLayoutName = "Title and Content";

        var customLayoutsByName = new Dictionary<string, CustomLayout>();
        foreach (CustomLayout layout in presentation.SlideMaster.CustomLayouts)
            customLayoutsByName[layout.Name] = layout;
        var layoutsForDeleting = new HashSet<string>();

        // Replace the incorrect layouts with the correct ones
        for (int slideNum = 1; slideNum <= presentation.Slides.Count; slideNum++)
        {
            Slide slide = presentation.Slides[slideNum];
            string oldLayoutName = slide.CustomLayout.Name;
            string newLayoutName = defaultLayoutName;
            if (layoutMappings.ContainsKey(oldLayoutName))
                newLayoutName = layoutMappings[oldLayoutName];
            if (newLayoutName != oldLayoutName)
            {
                Console.WriteLine($"  Replacing invalid slide layout \"{oldLayoutName}\" on slide #{slideNum} with \"{newLayoutName}\"");
                // Replace the old layout with the new layout
                slide.CustomLayout = customLayoutsByName[newLayoutName];
                layoutsForDeleting.Add(oldLayoutName);
            }
        }

        // Delete all old (and no longer used) layouts
        foreach (var layoutName in layoutsForDeleting)
        {
            Console.WriteLine($"  Deleting unused layout \"{layoutName}\"");
            CustomLayout layout = customLayoutsByName[layoutName];
            layout.Delete();
        }
    }

    static void FixSectionTitleSlides(Presentation presentation)
    {
        Console.WriteLine("Fixing broken section title slides...");

        var sectionTitleSlides = presentation.Slides.Cast<Slide>()
            .Where(slide => slide.CustomLayout.Name == "Section Title Slide");
        foreach (Slide slide in sectionTitleSlides)
        {
            // Collect the texts from the slide (expecting title and subtitle)
            List<string> slideTexts = new List<string>();
            foreach (Shape shape in slide.Shapes)
            {
                try
                {
                    if (shape.HasTextFrame == MsoTriState.msoTrue
                        && (shape.PlaceholderFormat.Type == PpPlaceholderType.ppPlaceholderTitle
                            || shape.PlaceholderFormat.Type == PpPlaceholderType.ppPlaceholderSubtitle
                            || shape.PlaceholderFormat.Type == PpPlaceholderType.ppPlaceholderBody)
                        && shape.TextFrame.TextRange.Text != "")
                    {
                        slideTexts.Add(shape.TextFrame.TextRange.Text);
                        shape.Delete();
                    }
                }
                catch (Exception)
                {
                    // Silently ignore --> the shape is not a placeholder
                }
            }

            // Put the slide texts into the placeholders (and delete the empty placeholders)
            for (int i = 0; i < slide.Shapes.Placeholders.Count; i++)
            {
                Shape placeholder = slide.Shapes.Placeholders[i+1];
                if (i < slideTexts.Count)
                    placeholder.TextFrame.TextRange.Text = slideTexts[i];
                else
                    placeholder.Delete();
            }
            Console.WriteLine($" Fixed slide #{slide.SlideNumber}: {slideTexts.FirstOrDefault()}");
        }
    }

    static void FixSlideTitles(Presentation presentation)
    {
        Console.WriteLine("Fixing incorrect slide titles...");

        var titleMappings = new Dictionary<string, string> {
            { "Table of Content", "Table of Contents" }
        };

        List<Shape> slideTitleShapes = 
            ExtractSlideTitleShapes(presentation, includeSubtitles: true);
        List<string> slideTitles = slideTitleShapes
            .Select(shape => shape?.TextFrame.TextRange.Text)
            .ToList();
        for (int i = 0; i < slideTitleShapes.Count; i++)
        {
            string newTitle = FixEnglishTitleCharacterCasing(slideTitles[i]);
            if (newTitle != null && titleMappings.ContainsKey(newTitle))
                newTitle = titleMappings[newTitle];
            if (newTitle != slideTitles[i])
            {
                Console.WriteLine($"  Replaced slide #{i} title: [{slideTitles[i]}] -> [{newTitle}]");
                slideTitleShapes[i].TextFrame.TextRange.Text = newTitle;
            }
        }
    }

    static Language DetectPresentationLanguage(Presentation presentation)
    {
        Console.WriteLine("Detecting presentation language...");

        var englishLettersCount = 0;
        var bulgarianLettersCount = 0;
        var slideTitles = ExtractSlideTitles(presentation);
        foreach (string title in slideTitles)
            if (title != null)
                foreach (char ch in title.ToLower())
                    if (ch >= 'a' && ch <= 'z')
                        englishLettersCount++;
                    else if (ch >= 'а' && ch <= 'я')
                        bulgarianLettersCount++;

        Language lang = (bulgarianLettersCount > englishLettersCount / 2) ?
            Language.BG : Language.EN;
        Console.WriteLine($"  Language detected: {lang}");
        return lang;
    }

    static void FixLicenseSlide(Presentation presentation, string pptTemplateFileName,
        List<string> pptTemplateSlideTitles, Language lang)
    {
        Console.WriteLine("Fixing the [License] slide...");
        string licenseSlideTitle =
            (lang == Language.EN) ? "License" : "Лиценз";
        int licenseSlideIndexInTemplate = 1;

        var slideTitles = ExtractSlideTitles(presentation);
        for (int slideNum = 1; slideNum <= presentation.Slides.Count; slideNum++)
        {
            if (slideTitles[slideNum - 1] == "License" ||
                slideTitles[slideNum - 1] == "Лиценз")
            {
                Console.WriteLine($"  Found the [License] slide #{slideNum} --> replacing it from the template");

                presentation.Slides[slideNum].Delete();
                presentation.Slides.InsertFromFile(pptTemplateFileName, slideNum - 1,
                    licenseSlideIndexInTemplate, licenseSlideIndexInTemplate);
            }
        }
    }

    static void FixSlideNumbers(Presentation presentation)
    {
        Shape FindFirstSlideNumberShape()
        {
            CustomLayout layout =
                presentation.SlideMaster.CustomLayouts.OfType<CustomLayout>()
                .Where(l => l.Name == "Title and Content").First();
            foreach (Shape shape in layout.Shapes)
                if (shape.Type == MsoShapeType.msoPlaceholder)
                    if (shape.PlaceholderFormat.Type == PpPlaceholderType.ppPlaceholderSlideNumber)
                        return shape;
            return null;
        }

        Console.Write("Fixing slide numbering...");

        var layoutsWithoutNumbering = new HashSet<string>() {
            "Presentation Title Slide",
            "Section Title Slide",
            "Questions Slide"
        };

        Shape slideNumberShape = FindFirstSlideNumberShape();
        slideNumberShape.Copy();

        // Delete the [slide number] box in each slide, then put it again if needed
        for (int slideNum = 1; slideNum <= presentation.Slides.Count; slideNum++)
        {
            Slide slide = presentation.Slides[slideNum];
            string layoutName = slide.CustomLayout.Name;

            foreach (Shape shape in slide.Shapes)
            {
                bool isSlideNumberTextBox = shape.Type == MsoShapeType.msoTextBox
                    && shape.Name.Contains("Slide Number");
                bool isSlideNumberPlaceholder = shape.Type == MsoShapeType.msoPlaceholder
                    && shape.PlaceholderFormat.Type == PpPlaceholderType.ppPlaceholderSlideNumber;
                if (isSlideNumberTextBox || isSlideNumberPlaceholder)
                {
                    // Found a "slide number" shape --> delete it
                    shape.Delete();
                }
            }

            if (!layoutsWithoutNumbering.Contains(layoutName))
            {
                // The slide should have [slide number] box --> insert it
                slide.Shapes.Paste();
            }

            Console.Write("."); // Display progress of the current operation
        }
        Console.WriteLine();
    }

    static void FixSlideNotesPages(Presentation presentation)
    {
        Shape FindNotesFooter()
        {
            var footerShape = presentation.NotesMaster.Shapes.OfType<Shape>().Where(
                shape => shape.Type == MsoShapeType.msoPlaceholder
                && shape.PlaceholderFormat.Type == PpPlaceholderType.ppPlaceholderFooter)
                .FirstOrDefault();
            return footerShape;
        }

        Console.WriteLine("Fixing slide notes pages...");

        Shape footerFromNotesMaster = FindNotesFooter();
        footerFromNotesMaster.Copy();

        for (int slideNum = 1; slideNum <= presentation.Slides.Count; slideNum++)
        {
            Slide slide = presentation.Slides[slideNum];
            if (slide.HasNotesPage == MsoTriState.msoTrue)
            {
                var slideNotesFooter = slide.NotesPage.Shapes.OfType<Shape>().Where(
                    shape => shape.Type == MsoShapeType.msoPlaceholder
                    && shape.PlaceholderFormat.Type == PpPlaceholderType.ppPlaceholderFooter)
                    .FirstOrDefault();
                if (slideNotesFooter != null)
                    slideNotesFooter.Delete();
                slide.NotesPage.Shapes.Paste();
            }
        }
    }
}
