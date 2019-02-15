﻿// Copyright (c) 2017 Katy Coe - https://www.djkaty.com - https://github.com/djkaty
// All rights reserved

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Il2CppInspector.Reflection;

namespace Il2CppInspector
{
  public class Il2CppDumper
  {
      private readonly Il2CppReflector model;

      public Il2CppDumper(Il2CppInspector proc) {
        model = new Il2CppReflector(proc);
      }

      public void WriteFiles(string outDir, string filter) {
        foreach (var asm in model.Assemblies) {
          string path = Path.Combine(outDir, $"{asm.FullName}.cs");
          using (var writer = new StreamWriter(new FileStream(path, FileMode.Create), Encoding.UTF8)) {
            writer.Write($"// Image {asm.Index}: {asm.FullName} - {asm.Definition.typeStart}\n");
            foreach (var type in asm.DefinedTypes) {
              if (filter != null && !type.Namespace.Contains(filter))
                continue;

              // Type declaration
              writer.Write($"\n// Namespace: {type.Namespace}\n");

              if (type.IsImport)
                writer.Write("[ComImport]");
              if (type.IsSerializable)
                writer.Write("[Serializable]\n");
              if (type.IsPublic || type.IsNestedPublic)
                writer.Write("public ");
              if (type.IsNestedPrivate)
                writer.Write("private ");
              if (type.IsNestedFamily)
                writer.Write("protected ");
              if (type.IsNestedAssembly || type.IsNotPublic)
                writer.Write("internal ");
              if (type.IsNestedFamORAssem)
                writer.Write("protected internal ");
              if (type.IsNestedFamANDAssem)
                writer.Write("[family and assembly] ");

              // Roll-up multicast delegates to use the 'delegate' syntactic sugar
              if (type.IsClass && type.IsSealed && type.BaseType?.FullName == "System.MulticastDelegate") {
                var del = type.DeclaredMethods.First(x => x.Name == "Invoke");
                writer.Write($"delegate {del.ReturnType.CSharpName} {type.Name}(");

                bool first = true;
                foreach (var param in del.DeclaredParameters) {
                  if (!first)
                    writer.Write(", ");
                  first = false;
                  if (param.IsOptional)
                    writer.Write("optional ");
                  if (param.IsOut)
                    writer.Write("out ");
                  writer.Write($"{param.ParameterType.CSharpName} {param.Name}");
                }
                writer.Write($"); // TypeDefIndex: {type.Index}; 0x{del.VirtualAddress:X8}\n");
                continue;
              }

              // An abstract sealed class is a static class
              if (type.IsAbstract && type.IsSealed)
                writer.Write("static ");
              else {
                if (type.IsAbstract && !type.IsInterface)
                  writer.Write("abstract ");
                if (type.IsSealed && !type.IsValueType && !type.IsEnum)
                  writer.Write("sealed ");
              }
              if (type.IsInterface)
                writer.Write("interface ");
              else if (type.IsValueType)
                writer.Write("struct ");
              else if (type.IsEnum)
                writer.Write("enum ");
              else
                writer.Write("class ");

              var @base = type.ImplementedInterfaces.Select(x => x.CSharpName).ToList();
              if (type.BaseType != null && type.BaseType.FullName != "System.Object" && type.BaseType.FullName != "System.ValueType" && !type.IsEnum)
                @base.Insert(0, type.BaseType.CSharpName);
              if (type.IsEnum && type.ElementType.CSharpName != "int") // enums derive from int by default
                @base.Insert(0, type.ElementType.CSharpName);
              var baseText = @base.Count > 0 ? " : " + string.Join(", ", @base) : string.Empty;

              writer.Write($"{type.Name}{baseText} // TypeDefIndex: {type.Index}\n{{\n");

              // Fields
              if (!type.IsEnum) {
                if (type.DeclaredFields.Count > 0)
                  writer.Write("\t// Fields\n");

                foreach (var field in type.DeclaredFields) {
                  writer.Write("\t");
                  if (field.IsNotSerialized)
                    writer.Write("[NonSerialized]\t");

                  if (field.IsPrivate)
                    writer.Write("private ");
                  if (field.IsPublic)
                    writer.Write("public ");
                  if (field.IsFamily)
                    writer.Write("protected ");
                  if (field.IsAssembly)
                    writer.Write("internal ");
                  if (field.IsFamilyOrAssembly)
                    writer.Write("protected internal ");
                  if (field.IsFamilyAndAssembly)
                    writer.Write("[family and assembly] ");
                  if (field.IsLiteral)
                    writer.Write("const ");
                  // All const fields are also static by implication
                  else if (field.IsStatic)
                    writer.Write("static ");
                  if (field.IsInitOnly)
                    writer.Write("readonly ");
                  if (field.IsPinvokeImpl)
                    writer.Write("extern ");
                  writer.Write($"{field.FieldType.CSharpName} {field.Name}");
                  if (field.HasDefaultValue)
                    writer.Write($" = {field.DefaultValueString}");
                  writer.Write("; // 0x{0:X2}\n", field.Offset);
                }
                if (type.DeclaredFields.Count > 0)
                  writer.Write("\n");
              }

              // Enumeration
              else {
                writer.Write(string.Join(",\n", type.GetEnumNames().Zip(type.GetEnumValues().OfType<object>(),
                                                                        (k, v) => new { k, v }).OrderBy(x => x.v).Select(x => $"\t{x.k} = {x.v}")) + "\n");
              }

              var usedMethods = new List<Reflection.MethodInfo>();

              // Properties
              if (type.DeclaredProperties.Count > 0)
                writer.Write("\t// Properties\n");

              foreach (var prop in type.DeclaredProperties) {
                string modifiers = prop.GetMethod?.GetModifierString() ?? prop.SetMethod.GetModifierString();
                writer.Write($"\t{modifiers}{prop.PropertyType.CSharpName} {prop.Name} {{ ");
                writer.Write((prop.GetMethod != null ? "get; " : "") + (prop.SetMethod != null ? "set; " : "") + "}");
                if ((prop.GetMethod != null && prop.GetMethod.VirtualAddress != 0) || (prop.SetMethod != null && prop.SetMethod.VirtualAddress != 0))
                  writer.Write(" // ");
                writer.Write((prop.GetMethod != null && prop.GetMethod.VirtualAddress != 0? "0x{0:X8} " : "")
                             + (prop.SetMethod != null && prop.SetMethod.VirtualAddress != 0? "0x{1:X8}" : "") + "\n",
                             prop.GetMethod?.VirtualAddress, prop.SetMethod?.VirtualAddress);
                usedMethods.Add(prop.GetMethod);
                usedMethods.Add(prop.SetMethod);
              }
              if (type.DeclaredProperties.Count > 0)
                writer.Write("\n");

              // Events
              if (type.DeclaredEvents.Count > 0)
                writer.Write("\t// Events\n");

              foreach (var evt in type.DeclaredEvents) {
                string modifiers = evt.AddMethod?.GetModifierString();
                writer.Write($"\t{modifiers}event {evt.EventHandlerType.CSharpName} {evt.Name} {{\n");
                var m = new Dictionary<string, uint>();
                if (evt.AddMethod != null) m.Add("add", evt.AddMethod.VirtualAddress);
                if (evt.RemoveMethod != null) m.Add("remove", evt.RemoveMethod.VirtualAddress);
                if (evt.RaiseMethod != null) m.Add("raise", evt.RaiseMethod.VirtualAddress);
                writer.Write(string.Join("\n", m.Select(x => $"\t\t{x.Key}; // 0x{x.Value:X8}")) + "\n\t}\n");
                usedMethods.Add(evt.AddMethod);
                usedMethods.Add(evt.RemoveMethod);
                usedMethods.Add(evt.RaiseMethod);
              }
              if (type.DeclaredEvents.Count > 0)
                writer.Write("\n");

              // Methods
              if (type.DeclaredMethods.Except(usedMethods).Any())
                writer.Write("\t// Methods\n");

              // Don't re-output methods for constructors, properties, events etc.
              foreach (var method in type.DeclaredMethods.Except(usedMethods)) {
                writer.Write($"\t{method.GetModifierString()}{method.ReturnType.CSharpName} {method.Name}(");

                bool first = true;
                foreach (var param in method.DeclaredParameters) {
                  if (!first)
                    writer.Write(", ");
                  first = false;
                  if (param.IsOptional)
                    writer.Write("optional ");
                  if (param.IsOut)
                    writer.Write("out ");
                  writer.Write($"{param.ParameterType.CSharpName} {param.Name}");
                }
                writer.Write(");" + (method.VirtualAddress != 0? $" // 0x{method.VirtualAddress:X8}" : "") + "\n");
              }
              writer.Write("}\n");
            }
          }
        }
      }
  }
}
