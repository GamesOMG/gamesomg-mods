// interop-gen: build Lord of Undead's interop assemblies offline, the way BepInEx be.793 does on the game's first start.
//   dotnet run -c Release -- <game folder> <output folder>
// Reads GameAssembly.dll and Lord of Undead_Data/il2cpp_data/Metadata/global-metadata.dat; never starts the game.
// Without Unity's base libraries (BepInEx downloads them at first start to un-strip methods the game's build dropped), so
// what this writes is the STRIPPED API: every member in it exists at run time too, and a member that isn't in it is one
// the mod must not use (the in-game interop may have it, un-stripped, but that is not proven per method).
using System;
using System.Collections.Generic;
using System.IO;
using AssetRipper.Primitives;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.InstructionSets;
using Cpp2IL.Core.OutputFormats;
using Cpp2IL.Core.ProcessingLayers;
using Il2CppInterop.Common;
using Il2CppInterop.Generator;
using Il2CppInterop.Generator.Runners;
using LibCpp2IL;
using Microsoft.Extensions.Logging;

if (args.Length < 2) { Console.Error.WriteLine("usage: interop-gen <game folder> <output folder>"); return 2; }
string game = args[0], outDir = args[1];
string ga = Path.Combine(game, "GameAssembly.dll");
string meta = Path.Combine(game, "Lord of Undead_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
if (!File.Exists(ga) || !File.Exists(meta)) { Console.Error.WriteLine("no GameAssembly.dll / global-metadata.dat under " + game); return 2; }

Cpp2IL.Core.Logging.Logger.InfoLog += (m, s) => Console.WriteLine("[Cpp2IL] [" + s + "] " + m.Trim());
Cpp2IL.Core.Logging.Logger.WarningLog += (m, s) => Console.WriteLine("[Cpp2IL] WARN [" + s + "] " + m.Trim());
Cpp2IL.Core.Logging.Logger.ErrorLog += (m, s) => Console.WriteLine("[Cpp2IL] ERROR [" + s + "] " + m.Trim());
InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_32);
InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_64);
LibCpp2IlBinaryRegistry.RegisterBuiltInBinarySupport();

var version = UnityVersion.Parse("6000.0.58f2");   // the game's globalgamemanagers (claude-memory/games/lord-of-undead/map.md)
Cpp2IlApi.InitializeLibCpp2Il(ga, meta, version);
var layers = new List<Cpp2IlProcessingLayer> { new AttributeInjectorProcessingLayer() };
foreach (var l in layers) l.PreProcess(Cpp2IlApi.CurrentAppContext, layers);
foreach (var l in layers) l.Process(Cpp2IlApi.CurrentAppContext);
var source = new AsmResolverDllOutputFormatDefault().BuildAssemblies(Cpp2IlApi.CurrentAppContext);
LibCpp2IlMain.Reset();
Cpp2IlApi.CurrentAppContext = null;
Console.WriteLine("Cpp2IL: " + source.Count + " dummy assemblies");

Directory.CreateDirectory(outDir);
foreach (var f in Directory.EnumerateFiles(outDir, "*.dll")) File.Delete(f);
var opts = new GeneratorOptions { GameAssemblyPath = ga, Source = source, OutputDir = outDir, UnityBaseLibsDir = null };
Il2CppInteropGenerator.Create(opts).AddLogger(new ConsoleLog()).AddInteropAssemblyGenerator().Run();
Console.WriteLine("interop written to " + outDir + ": " + Directory.GetFiles(outDir, "*.dll").Length + " assemblies");
return 0;

class ConsoleLog : ILogger
{
    public IDisposable BeginScope<T>(T state) => null;
    public bool IsEnabled(LogLevel l) => l >= LogLevel.Information;
    public void Log<T>(LogLevel l, EventId id, T state, Exception e, Func<T, Exception, string> f)
    { if (IsEnabled(l)) Console.WriteLine("[Il2CppInteropGen] " + f(state, e)); }
}
