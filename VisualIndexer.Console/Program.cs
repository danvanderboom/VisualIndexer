using System;
using SkiaSharp;
using Spectre.Console;
using VisualIndexer;

AnsiConsole.Write(
        new FigletText("Visual Indexer")
            .Centered()
            .Color(Color.DodgerBlue2));

Console.WriteLine();

var random = new Random();

var indexer = new Indexer();

var pageCount = 12;
var fullImageWidth = 600;
var fullImageHeight = 800;

var testImages = GenerateRandomColorBitmaps(pageCount, fullImageWidth, fullImageHeight);

var layout = indexer.CalculateGridLayout(pageCount, fullImageWidth, fullImageHeight);

var gridMap = indexer.CreateMapOfGridCellsToPages(startPage: 1, pageCount: pageCount, layout.Rows, layout.Columns);
DisplayPageNumberForCellId("A1");
DisplayPageNumberForCellId("E1");
DisplayPageNumberForCellId("C2");

var grid = indexer.CreatePageGridImage(layout, testImages);

// save grid image
using (var image = SKImage.FromBitmap(grid))
{
    using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
    {
        using (var stream = File.OpenWrite("spreadsheet_grid.png"))
        {
            data.SaveTo(stream);
        }
    }
}

Console.WriteLine("Spreadsheet grid image has been created.");



void DisplayPageNumberForCellId(string cellId)
{
    Console.WriteLine($"Grid cell {cellId} = page {gridMap[cellId]}");
}

List<SKBitmap> GenerateRandomColorBitmaps(int count, int width, int height)
{
    var bitmaps = new List<SKBitmap>();

    for (int i = 0; i < count; i++)
    {
        // Generate a random color, ensuring it is not white
        SKColor color;
        do
        {
            color = new SKColor(
                (byte)random.Next(256),
                (byte)random.Next(256),
                (byte)random.Next(256));
        } while (color == SKColors.White); // Repeat if the color is white

        // Create a new bitmap and paint it with the random color
        var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        bitmaps.Add(bitmap);
    }

    return bitmaps;
}

