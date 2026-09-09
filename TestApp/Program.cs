using System;
using System.IO;
using Tesseract;

class Program {
    static void Main() {
        try {
            var tessDataPath = Path.Combine(@"C:\Users\duran\repos\dunetimer\DuneTimer\bin\Debug\net9.0-windows10.0.19041.0\win-x64", "tessdata");
            Console.WriteLine($"Path: {tessDataPath}");
            Console.WriteLine($"Exists: {Directory.Exists(tessDataPath)}");
            var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
            Console.WriteLine("Success!");
        } catch (Exception ex) {
            Console.WriteLine("ERROR:");
            Console.WriteLine(ex.ToString());
        }
    }
}
