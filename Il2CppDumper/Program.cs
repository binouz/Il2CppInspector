﻿// Copyright (c) 2017 Katy Coe - https://www.djkaty.com - https://github.com/djkaty
// All rights reserved

using System;
using System.IO;

namespace Il2CppInspector
{
  public class App
  {
      static void Main(string[] args) {

        // Command-line usage: dotnet run [<binary-file> [<metadata-file> [<output-file>]]]
        // Defaults to libil2cpp.so or GameAssembly.dll if binary file not specified
        string imageFile = "libil2cpp.so";
        string metaFile = "global-metadata.dat";
        string outDir = "types";
        string filter = null;

        if (args.Length == 0)
          if (!File.Exists(imageFile))
            imageFile = "GameAssembly.dll";

        if (args.Length >= 1)
          imageFile = args[0];

        if (args.Length >= 2)
          metaFile = args[1];

        if (args.Length >= 3)
          outDir = args[2];

        if (args.Length >= 4)
          filter = args[3];

        // Check files
        if (!File.Exists(imageFile)) {
          Console.Error.WriteLine($"File {imageFile} does not exist");
          Environment.Exit(1);
        }
        if (!File.Exists(metaFile)) {
          Console.Error.WriteLine($"File {metaFile} does not exist");
          Environment.Exit(1);
        }
        if (!Directory.Exists(outDir)) {
          Directory.CreateDirectory(outDir);
        }

        // Analyze data
        var il2cppInspectors = Il2CppInspector.LoadFromFile(imageFile, metaFile);
        if (il2cppInspectors == null)
          Environment.Exit(1);

        // Write output file
        int i = 0;
        foreach (var il2cpp in il2cppInspectors)
          new Il2CppDumper(il2cpp).WriteFiles(outDir + (i++ > 0 ? "-" + (i-1) : ""), filter);
      }
  }
}
