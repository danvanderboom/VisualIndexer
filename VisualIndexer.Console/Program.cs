using System;
using System.Data;
using SkiaSharp;
using Spectre.Console;
using ImageMagick;
using UglyToad.PdfPig;
using VisualIndexer;

AnsiConsole.Write(
    new FigletText("Visual Indexer")
        .Centered()
        .Color(Color.DodgerBlue2));

Console.WriteLine();

var documentPath = args[0];
if (!Path.Exists(documentPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Error: file does not exist ({documentPath})");
    return;
}

var random = new Random();

var indexer = new Indexer();

var pageBatchSize = 12;
var batchIndex = 0;

await ProcessPdfPageBatches(documentPath, pageBatchSize, async batch =>
{
    var startPage = batch.First().Key;
    var pageCount = batch.Count;
    var pageImages = batch.Select(b => b.Value).ToList();
    var imageWidth = pageImages.First().Width;
    var imageHeight = pageImages.First().Height;

    var batchGridLayout = indexer.CalculateGridLayout(pageCount, imageWidth, imageHeight);
    var batchGridCellToPageMap = indexer.CreateGridCellToPageMap(1, pageCount, batchGridLayout.Rows, batchGridLayout.Columns);

    var grid = indexer.CreatePageGridImage(batchGridLayout, pageImages);
    SaveGridImage($"pdfgrid.{startPage}-{(startPage + pageCount - 1)}.png", grid);

    batchIndex++;
});




void SaveGridImage(string fileName, SKBitmap grid)
{
    using (var image = SKImage.FromBitmap(grid))
    {
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            using (var stream = File.OpenWrite(fileName))
            {
                data.SaveTo(stream);
            }
        }
    }
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

string GetRandomGridCell(GridLayout layout, int totalNumberOfPages)
{
    var page = random.Next(totalNumberOfPages + 1);
    var pageToGridCellMap = indexer.CreatePageToGridCellMap(1, totalNumberOfPages, layout.Rows, layout.Columns);
    return pageToGridCellMap[page];
}

async Task ProcessPdfPageBatches(string pdfFilePath, int batchSize, Func<List<KeyValuePair<int, SKBitmap>>, Task> processBatch)
{
    var settings = new MagickReadSettings { Density = new Density(300, 300) };

    int pageNumber = 1;

    using var document = PdfDocument.Open(pdfFilePath);
    var totalPageCount = document.NumberOfPages;

    while (pageNumber <= totalPageCount)
    {
        var batch = new List<KeyValuePair<int, SKBitmap>>();

        for (int i = 0; i < batchSize && pageNumber <= totalPageCount; i++, pageNumber++)
        {
            // read only the current page
            using var magickImage = new MagickImage($"{pdfFilePath}[{pageNumber - 1}]", settings); // Page numbers in settings are 0-based
            var bitmap = ConvertMagickImageToSKBitmap(magickImage);

            // add the bitmap with its page number to the batch
            batch.Add(new KeyValuePair<int, SKBitmap>(pageNumber, bitmap));
        }

        // process the current batch
        if (batch.Count > 0)
            await processBatch(batch);
    }
}

SKBitmap ConvertMagickImageToSKBitmap(MagickImage magickImage)
{
    // Read pixel data from MagickImage
    byte[]? pixels = magickImage.GetPixels().ToByteArray(0, 0, magickImage.Width, magickImage.Height, "RGBA");
    if (pixels == null)
        throw new Exception("No pixels returned from Magick image.");

    // Create a new SKBitmap with the same dimensions as the MagickImage
    var info = new SKImageInfo(magickImage.Width, magickImage.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
    var skBitmap = new SKBitmap(info);

    // Lock the SKBitmap's pixels for writing
    using (var pixmap = new SKPixmap(info, skBitmap.GetPixels()))
    {
        // Copy the pixel data into the SKBitmap
        var len = new IntPtr(pixels.Length);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, pixmap.GetPixels(), pixels.Length);
    }

    return skBitmap;
}