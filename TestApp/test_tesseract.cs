using System;
using System.IO;
using Tesseract;

class Program {
    static void Main() {
        try {
            var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
            Console.WriteLine($"Path: {tessDataPath}");
            Console.WriteLine($"Exists: {Directory.Exists(tessDataPath)}");
            var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
            Console.WriteLine("Success!");
        } catch (Exception ex) {
            Console.WriteLine(ex.ToString());
        }
    }
}
