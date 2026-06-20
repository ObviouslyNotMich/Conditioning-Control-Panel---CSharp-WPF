using System;
using System.Reflection;
class P { static void Main() { var asm = Assembly.LoadFrom(@"C:\Users\Micha\.nuget\packages\libvlcsharp\3.9.7.1\lib\net8.0\LibVLCSharp.dll"); var t = asm.GetType("LibVLCSharp.Shared.Structures.AudioOutputDescription"); foreach (var f in t.GetFields()) Console.WriteLine(f.FieldType.Name + " " + f.Name); } }
