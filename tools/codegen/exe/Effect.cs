// Copyright (c) Microsoft Corporation. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may
// not use these files except in compliance with the License. You may obtain
// a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
// WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
// License for the specific language governing permissions and limitations
// under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Xml.Serialization;
using System.IO;

namespace CodeGen
{
    public class Field
    {
        public Field()
        {
            ShouldProject = true;
        }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("displayname")]
        public string Displayname { get; set; }

        [XmlAttribute("index")]
        public string Index { get; set; }

        public bool ShouldProject { get; set; }
    }

    public class Fields
    {
        public Fields()
        {
            IsUnique = true;
            IsRepresentative = false;
        }

        public D2DEnum NativeEnum { get; set; }

        public bool IsUnique { get; set; }

        public bool IsRepresentative { get; set; }

        [XmlElement("Field")]
        public List<Field> FieldsList { get; set; }

        public override bool Equals(System.Object obj)
        {
            // If parameter is null return false.
            if (obj == null)
            {
                return false;
            }

            // If parameter cannot be cast to Point return false.
            Fields fields = obj as Fields;
            if ((System.Object)fields == null)
            {
                return false;
            }

            if (fields.FieldsList.Count != this.FieldsList.Count)
            {
                return false;
            }

            // Return true if the fields match:
            bool isSame = true;
            for (int fieldCount = 0; fieldCount < fields.FieldsList.Count; ++fieldCount)
            {
                if (fields.FieldsList[fieldCount].Name != this.FieldsList[fieldCount].Name)
                {
                    isSame = false;
                    break;
                }
            }
            return isSame;
        }

        public override int GetHashCode()
        {
            int hash = 0;

            foreach (var field in FieldsList)
            {
                hash ^= field.GetHashCode();
            }

            return hash;
        }
    }

    public class Property
    {
        public Property()
        {
            ShouldProject = true;
        }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("type")]
        public string Type { get; set; }

        [XmlAttribute("value")]
        public string Value { get; set; }

        [XmlElement("Property")]
        public List<Property> Properties { get; set; }

        // Used only for enum types
        [XmlElement("Fields")]
        public Fields EnumFields { get; set; }

        public string EffectName { get; set; }

        public string TypeNameIdl { get; set; }
        public string TypeNameCpp { get; set; }
        public string TypeNameBoxed { get; set; }

        public bool ShouldProject { get; set; }
        public bool IsHidden { get; set; }
        public bool IsHandCoded { get; set; }

        public string NativePropertyName { get; set; }

        public List<string> ExcludedEnumIndexes { get; set; }
    }

    public class Input
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("minimum")]
        public string Minimum { get; set; }

        [XmlAttribute("maximum")]
        public string Maximum { get; set; }
    }

    public class Inputs
    {
        [XmlElement("Input")]
        public List<Input> InputsList { get; set; }

        [XmlAttribute("minimum")]
        public string Minimum { get; set; }

        [XmlAttribute("maximum")]
        public string Maximum { get; set; }
    }

    [XmlRoot("Effect")]
    public class Effect
    {
        [XmlElement("Property")]
        public List<Property> Properties { get; set; }

        [XmlElement("Inputs")]
        public Inputs Inputs { get; set; }

        public string ClassName { get; set; }

        public string InterfaceName { get; set; }

        public string Uuid { get; set; }
    }

    public class D2DEnum
    {
        public D2DEnum()
        {
            Enums = new List<string>();
        }

        public string Name { get; set; }
        public List<string> Enums { get; set; }
    }

    public static class EffectGenerator
    {
        public static void OutputEffects(string inputEffectsDir, string outputPath)
        {
            string[] filePaths = Directory.GetFiles(inputEffectsDir);

            var overridesXmlData = XmlBindings.Utilities.LoadXmlData<Overrides.XmlBindings.Settings>(inputEffectsDir, "../../Settings.xml");

            List<Effect> effects = new List<Effect>();
            foreach (var xmlFilePath in filePaths)
            {
                effects.Add(ParseEffectXML(xmlFilePath));
            }

            string windowsKitPath = Environment.ExpandEnvironmentVariables(@"%WindowsSdkDir%");

            // Check if %WindowsSdkDir% path is set
            if (!Directory.Exists(windowsKitPath))
            {
                // Try the default path 
                windowsKitPath = @"C:\Program Files (x86)\Windows Kits\8.1";
                if (!Directory.Exists(windowsKitPath))
                {
                    throw new Exception(@"Missing WindowsSdkDir environment variable. Please run this application from VS command prompt");
                }
            }

            List<string> d2dHeaders = new List<string>
            {
                windowsKitPath + @"/Include/um/d2d1effects.h",
                windowsKitPath + @"/Include/um/d2d1_1.h"
            };

            AssignEffectsNamesToProperties(effects);
            DetectCommonEnums(effects);
            AssignPropertyNames(effects);

            var overrides = overridesXmlData.Namespaces.Find(namespaceElement => namespaceElement.Name == "Effects");

            List<D2DEnum> d2dEnums = ParseD2DEffectsEnums(d2dHeaders);

            AssignD2DEnums(effects, d2dEnums);
            AssignEffectsClassNames(effects, overrides.Effects);
            ResolveTypeNames(effects);
            RegisterUuids(effects);
            OverrideEnums(overrides.Enums, effects);
            GenerateOutput(effects, outputPath);
        }

        // Register effect uuids. These are generated by hashing the interface name following RFC 4122.
        private static void RegisterUuids(List<Effect> effects)
        {
            var salt = new Guid("8DEBAF20-F852-4B20-BE55-4D7EA6DE19BE");

            foreach (var effect in effects)
            {
                var name = "Microsoft.Graphics.Canvas.Effects." + effect.InterfaceName;
                var uuid = UuidHelper.GetVersion5Uuid(salt, name);
                effect.Uuid = uuid.ToString().ToUpper();
            }
        }

        public static bool IsEffectEnabled(Effect effect)
        {
            switch (effect.Properties[0].Value)
            {
                // TODO #831: this effect requires Matrix5x4 support.
                case "Color Matrix":
                    return false;

                // TODO #2577: these effects require Blob support.
                case "Convolve Matrix":
                case "Discrete Transfer":
                case "Histogram":
                case "Table Transfer":
                    return false;

                default:
                    return true;
            }
        }

        private static List<Property> GetAllEffectsProperties(List<Effect> effects)
        {
            List<Property> allProperties = new List<Property>();
            foreach (var effect in effects)
            {
                foreach (var property in effect.Properties)
                {
                    allProperties.Add(property);
                }
            }
            return allProperties;
        }

        private static void AssignEffectsNamesToProperties(List<Effect> effects)
        {
            foreach (var effect in effects)
            {
                foreach (var property in effect.Properties)
                {
                    // Zero property in xml effect description corresponds to effect name
                    property.EffectName = effect.Properties[0].Value;
                }
            }
        }

        private static void OverrideEnums(List<Overrides.XmlBindings.Enum> enumsOverrides, List<Effect> effects)
        {
            foreach (var property in GetAllEffectsProperties(effects))
            {
                var enumOverride = enumsOverrides.Find(element => element.Name == property.TypeNameIdl);
                if (enumOverride != null)
                {
                    if (enumOverride.ProjectedNameOverride != null)
                    {
                        property.TypeNameIdl = enumOverride.ProjectedNameOverride;
                        if (enumOverride.Namespace != null)
                        {
                            property.TypeNameIdl = enumOverride.Namespace + "." + property.TypeNameIdl;
                        }
                        property.TypeNameCpp = enumOverride.ProjectedNameOverride;
                    }
                    property.ShouldProject = enumOverride.ShouldProject;
                    foreach (var enumValue in enumOverride.Values)
                    {
                        if (!enumValue.ShouldProject)
                        {
                            property.ExcludedEnumIndexes.Add(enumValue.Index);
                        }
                        if (enumValue.ProjectedNameOverride != null)
                        {
                            var enumToOverride = property.EnumFields.FieldsList.Find(enumEntry => enumEntry.Name == enumValue.Name);
                            if (enumToOverride != null)
                            {
                                enumToOverride.Name = enumValue.ProjectedNameOverride;
                            }
                        }
                    }
                }
            }
        }

        private static void ResolveTypeNames(List<Effect> effects)
        {
            var typeRenames = new Dictionary<string, string[]>
            {
                // D2D name                 IDL name   C++ name
                { "bool",   new string[] { "boolean", "boolean"  } },
                { "int32",  new string[] { "INT32",   "int32_t"  } },
                { "uint32", new string[] { "UINT32",  "uint32_t" } },
            };

            foreach (var property in GetAllEffectsProperties(effects))
            {
                if (property.TypeNameIdl != null)
                {
                    string xmlName = property.TypeNameIdl;

                    if (typeRenames.ContainsKey(xmlName))
                    {
                        // Specially remapped type, where D2D format XML files don't match WinRT type naming.
                        property.TypeNameIdl = typeRenames[xmlName][0];
                        property.TypeNameCpp = typeRenames[xmlName][1];
                        property.TypeNameBoxed = typeRenames[xmlName][1];
                    }
                    else if (xmlName.StartsWith("matrix") || xmlName.StartsWith("vector"))
                    {
                        if (property.Name.Contains("Rect"))
                        {
                            // D2D passes rectangle properties as float4, but we remap them to use strongly typed Rect.
                            property.TypeNameIdl = "Windows.Foundation.Rect";
                            property.TypeNameCpp = "Rect";
                        }
                        else if (property.Name.Contains("Color"))
                        {
                            // D2D passes color properties as float3 or float4, but we remap them to use strongly typed Color.
                            property.TypeNameIdl = "Windows.UI.Color";
                            property.TypeNameCpp = "Color";
                        }
                        else
                        {
                            // Vector or matrix type.
                            property.TypeNameIdl = "Microsoft.Graphics.Canvas.Numerics." + char.ToUpper(xmlName[0]) + xmlName.Substring(1);
                            property.TypeNameCpp = "Numerics::" + char.ToUpper(xmlName[0]) + xmlName.Substring(1);
                        }

                        // Convert eg. "matrix3x2" to 6, or "vector3" to 3.
                        var sizeSuffix = xmlName.SkipWhile(char.IsLetter).ToArray();
                        var sizeElements = new string(sizeSuffix).Split('x').Select(int.Parse);
                        var size = sizeElements.Aggregate((a, b) => a * b);

                        property.TypeNameBoxed = "float[" + size + "]";
                    }
                    else
                    {
                        // Any other type.
                        property.TypeNameCpp = xmlName;
                        property.TypeNameBoxed = xmlName;

                        // Enums are internally stored as uints.
                        if (property.Type == "enum")
                        {
                            property.TypeNameBoxed = "uint32_t";
                        }
                    }
                }
            }
        }

        // Determine if enums are unique to specific effect
        // or general for all effects
        private static void DetectCommonEnums(List<Effect> effects)
        {
            List<Property> allProperties = GetAllEffectsProperties(effects);
            for (int propertyIndex = 0; propertyIndex < allProperties.Count; propertyIndex++)
            {
                Fields fields = allProperties[propertyIndex].EnumFields;
                if (fields == null || fields.IsUnique == false)
                    continue;
                for (int propertyIndex2 = propertyIndex + 1; propertyIndex2 < allProperties.Count; propertyIndex2++)
                {
                    Fields fields2 = allProperties[propertyIndex2].EnumFields;
                    if (fields2 == null || fields.FieldsList.Count != fields2.FieldsList.Count)
                        continue;
                    if (fields.Equals(fields2))
                    {
                        fields.IsUnique = false;
                        fields.IsRepresentative = true;
                        fields2.IsUnique = false;
                        fields2.FieldsList = fields.FieldsList;
                    }
                }
            }
        }

        private static void AssignPropertyNames(List<Effect> effects)
        {
            foreach (var property in GetAllEffectsProperties(effects))
            {
                string className = property.EffectName.Replace(" ", "") + "Effect";
                property.TypeNameIdl = property.Type;
                if (property.TypeNameIdl == "enum")
                {
                    if (!property.EnumFields.IsUnique)
                        property.TypeNameIdl = "Effect" + property.Name;
                    else
                        property.TypeNameIdl = className + property.Name;
                }

                property.NativePropertyName = "D2D1_" + property.EffectName.Replace(" ", "").Replace("-", "").ToUpper() + "_PROP";
                foreach (Char c in property.Name)
                {
                    if (Char.IsUpper(c))
                    {
                        property.NativePropertyName += "_";
                    }
                    property.NativePropertyName += c.ToString().ToUpper();
                }
            }
        }

        // Some effects have names that starts with 3D or 2D prefix.
        // C++ forbidds class name that starts with digits
        // Replace 3D/2D prefix at the end
        private static void AssignEffectsClassNames(List<Effect> effects, List<Overrides.XmlBindings.Effect> overrides)
        {
            foreach (var effect in effects)
            {
                string className = FormatClassName(effect.Properties[0].Value);

                string prefix = className.Substring(0, 2);
                if (prefix == "2D" || prefix == "3D")
                {
                    effect.ClassName = className.Remove(0, 2) + prefix + "Effect";
                }
                else
                {
                    effect.ClassName = className + "Effect";
                }

                // Apply effect and property name overrides based on XML settings
                var effectOverride = overrides.Find(o => o.Name == effect.ClassName);
                if (effectOverride != null)
                {
                    ApplyEffectOverrides(effect, effectOverride);
                }

                effect.InterfaceName = "I" + effect.ClassName;
            }
        }

        private static void ApplyEffectOverrides(Effect effect, Overrides.XmlBindings.Effect effectOverride)
        {
            // Override the effect name?
            if (effectOverride.ProjectedNameOverride != null)
            {
                effect.ClassName = effectOverride.ProjectedNameOverride;
            }

            // Override input names?
            foreach (var inputOverride in effectOverride.Inputs)
            {
                var input = effect.Inputs.InputsList.Find(p => p.Name == inputOverride.Name);
                input.Name = inputOverride.ProjectedNameOverride;
            }

            foreach (var propertyOverride in effectOverride.Properties)
            {
                var property = effect.Properties.Find(p => p.Name == propertyOverride.Name);

                if (property != null)
                {
                    // Override settings of an existing property.
                    if (propertyOverride.ProjectedNameOverride != null)
                    {
                        property.Name = propertyOverride.ProjectedNameOverride;
                    }

                    property.IsHidden = propertyOverride.IsInternal;
                }
                else
                {
                    // Add a custom property that is part of our API surface but not defined by D2D.
                    effect.Properties.Add(new Property
                    {
                        Name = propertyOverride.Name,
                        TypeNameIdl = propertyOverride.Type,
                        IsHandCoded = true
                    });
                }
            }
        }

        private static List<D2DEnum> ParseD2DEffectsEnums(List<string> pathsToHeaders)
        {
            List<D2DEnum> d2dEnums = new List<D2DEnum>();

            foreach (var path in pathsToHeaders)
            {
                StreamReader reader = File.OpenText(path);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains("typedef enum") && line.Substring(line.Length - 4) != "PROP")
                    {
                        D2DEnum effectEnum = new D2DEnum();
                        string[] words = line.Split(' ');
                        effectEnum.Name = words[words.Length - 1];

                        // Skip brace
                        reader.ReadLine();

                        while ((line = reader.ReadLine()) != "")
                        {
                            words = line.Split(' ');
                            // Indent size in headers is 8
                            if (words[8] != "" && words[8] != "//")
                                effectEnum.Enums.Add(words[8]);
                        }

                        // Remove last force_dword enum
                        effectEnum.Enums.RemoveAt(effectEnum.Enums.Count - 1);
                        d2dEnums.Add(effectEnum);
                    }
                }
            }

            return d2dEnums;
        }

        private static bool IsEnumEqualD2DEnum(Property enumProperty, D2DEnum d2dEnum, bool shouldMatchName)
        {
            // Check if names are the same
            if (FormatEnumValueString(d2dEnum.Name).Contains(FormatEnumValueString(enumProperty.EffectName)) || !shouldMatchName)
            {
                // Check if number of enums values are the same
                if (d2dEnum.Enums.Count == enumProperty.EnumFields.FieldsList.Count)
                {
                    for (int i = 0; i < enumProperty.EnumFields.FieldsList.Count; ++i)
                    {
                        if (!FormatEnumValueString(d2dEnum.Enums[i]).Contains(FormatEnumValueString(enumProperty.EnumFields.FieldsList[i].Displayname)))
                        {
                            return false;
                        }
                    }
                    return true;
                }
            }
            return false;
        }

        private static void AssignD2DEnums(List<Effect> effects, List<D2DEnum> d2dEnums)
        {
            foreach (var property in GetAllEffectsProperties(effects))
            {
                Fields fields = property.EnumFields;
                if (fields == null)
                    continue;

                // Try to find enum for specific effect
                foreach (var d2dEnum in d2dEnums)
                {
                    if (IsEnumEqualD2DEnum(property, d2dEnum, true))
                    {
                        fields.NativeEnum = d2dEnum;
                    }
                }

                // If failed to find enum for specific effect, try general
                if (fields.NativeEnum == null)
                {
                    // Try to find enum without name matching
                    foreach (var d2dEnum in d2dEnums)
                    {
                        if (IsEnumEqualD2DEnum(property, d2dEnum, false))
                        {
                            fields.NativeEnum = d2dEnum;
                        }
                    }
                }
            }
        }

        private static void GenerateOutput(List<Effect> effects, string outDirectory)
        {
            using (Formatter commonStreamWriter = new Formatter(Path.Combine(outDirectory, "EffectsCommon.abi.idl")))
            {
                OutputEffectType.OutputCommonEnums(effects, commonStreamWriter);
            }

            foreach (var effect in effects.Where(IsEffectEnabled))
            {
                Directory.CreateDirectory(outDirectory);
                using (Formatter idlStreamWriter = new Formatter(Path.Combine(outDirectory, effect.ClassName + ".abi.idl")),
                                 hStreamWriter = new Formatter(Path.Combine(outDirectory, effect.ClassName + ".h")),
                                 cppStreamWriter = new Formatter(Path.Combine(outDirectory, effect.ClassName + ".cpp")))
                {
                    OutputEffectType.OutputEffectIdl(effect, idlStreamWriter);
                    OutputEffectType.OutputEffectHeader(effect, hStreamWriter);
                    OutputEffectType.OutputEffectCpp(effect, cppStreamWriter);
                }
            }
        }

        private static string FormatEnumValueString(string inString)
        {
            string result = inString.Replace("_", "");
            result = result.Replace(" ", "");
            result = result.Replace("-", "");
            result = result.ToLower();

            return result;
        }

        public static string FormatClassName(string name)
        {
            return name.Replace(" ", "")
                       .Replace("-", "")
                       .Replace("DPI", "Dpi")
                       .Replace("toAlpha", "ToAlpha");
        }

        private static Effect ParseEffectXML(string path)
        {
            Effect effect = null;

            XmlSerializer serializer = new XmlSerializer(typeof(Effect));

            using (StreamReader reader = new StreamReader(path))
            {
                effect = (Effect)serializer.Deserialize(reader);
            }

            return effect;
        }
    }
}
