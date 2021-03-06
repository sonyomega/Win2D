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
using System.Text.RegularExpressions;

namespace CodeGen
{
    public static class OutputEffectType
    {
        const string stdInfinity = "std::numeric_limits<float>::infinity()";

        public static void OutputEnum(Property enumProperty, Formatter output)
        {
            output.WriteLine("[version(VERSION)]");
            output.WriteLine("typedef enum " + enumProperty.TypeNameIdl);
            output.WriteLine("{");
            output.Indent();

            for (int i = 0; i < enumProperty.EnumFields.FieldsList.Count; ++i)
            {
                var enumValue = enumProperty.EnumFields.FieldsList[i];
                if (i != (enumProperty.EnumFields.FieldsList.Count - 1))
                {
                    output.WriteLine(enumValue.Name + " = " + i.ToString() + ",");
                }
                else
                {
                    output.WriteLine(enumValue.Name + " = " + i.ToString());
                }
            }
            output.Unindent();
            output.WriteLine("} " + enumProperty.TypeNameIdl + ";");
        }

        public static void OutputCommonEnums(List<Effect> effects, Formatter output)
        {
            OutputDataTypes.OutputLeadingComment(output);

            output.WriteLine("namespace Microsoft.Graphics.Canvas.Effects");
            output.WriteLine("{");
            output.Indent();
            bool isFirstOutput = true;
            foreach (var effect in effects)
            {
                foreach (var property in effect.Properties)
                {
                    // Check if property represent enum that is common to some group of effects
                    if (property.Type == "enum" && property.EnumFields.IsRepresentative && property.ShouldProject)
                    {
                        var registeredEffects = effects.Where(EffectGenerator.IsEffectEnabled);
                        // Check if any registred property need this enum
                        if (registeredEffects.Any(e => e.Properties.Any(p => p.TypeNameIdl == property.TypeNameIdl)))
                        {
                            if (isFirstOutput)
                            {
                                isFirstOutput = false;
                            }
                            else
                            {
                                output.WriteLine();
                            }
                            OutputEnum(property, output);
                        }
                    }
                }
            }
            output.Unindent();
            output.WriteLine("}");
        }

        public static void OutputEffectIdl(Effect effect, Formatter output)
        {
            OutputDataTypes.OutputLeadingComment(output);

            output.WriteLine("namespace Microsoft.Graphics.Canvas.Effects");
            output.WriteLine("{");
            output.Indent();

            // Output all enums specific to this effect
            foreach (var property in effect.Properties)
            {
                if (property.Type == "enum" && property.EnumFields.IsUnique && property.ShouldProject)
                {
                    OutputEnum(property, output);
                    output.WriteLine();
                }
            }

            output.WriteLine("runtimeclass " + effect.ClassName + ";");
            output.WriteLine();
            output.WriteLine("[version(VERSION), uuid(" + effect.Uuid + "), exclusiveto(" + effect.ClassName + ")]");
            output.WriteLine("interface " + effect.InterfaceName + " : IInspectable");
            output.WriteIndent();
            output.WriteLine("requires  Microsoft.Graphics.Canvas.ICanvasImage");
            output.WriteLine("{");
            output.Indent();

            foreach (var property in effect.Properties)
            {
                // Property with type string describes 
                // name/author/category/description of effect but not input type
                if (property.Type == "string" || property.IsHidden)
                    continue;

                output.WriteLine("[propget]");
                output.WriteLine("HRESULT " + property.Name + "([out, retval] " + property.TypeNameIdl + "* value);");
                output.WriteLine();

                output.WriteLine("[propput]");
                output.WriteLine("HRESULT " + property.Name + "([in] " + property.TypeNameIdl + " value);");
                output.WriteLine();
            }

            // Check if inputs specify maximum attribute in xml and if it is marked as unlimited
            if (!(effect.Inputs.Maximum != null && effect.Inputs.Maximum == "0xFFFFFFFF"))
            {
                for (int i = 0; i < effect.Inputs.InputsList.Count; ++i)
                {
                    var input = effect.Inputs.InputsList[i];

                    output.WriteLine("[propget]");
                    output.WriteLine("HRESULT " + input.Name + "([out, retval] IEffectInput** input);");
                    output.WriteLine();

                    output.WriteLine("[propput]");
                    output.WriteLine("HRESULT " + input.Name + "([in] IEffectInput* input);");
                    output.WriteLine();
                }
            }

            output.Unindent();
            output.WriteLine("};");
            output.WriteLine();

            output.WriteLine("[version(VERSION), activatable(VERSION)]");
            output.WriteLine("runtimeclass " + effect.ClassName);
            output.WriteLine("{");
            output.Indent();
            output.WriteLine("[default] interface " + effect.InterfaceName + ";");
            output.WriteLine("interface IEffect;");
            output.Unindent();
            output.WriteLine("}");
            output.Unindent();
            output.WriteLine("}");
        }

        public static void OutputEffectHeader(Effect effect, Formatter output)
        {
            OutputDataTypes.OutputLeadingComment(output);

            output.WriteLine("#pragma once");
            output.WriteLine();
            output.WriteLine("namespace ABI { namespace Microsoft { namespace Graphics { namespace Canvas { namespace Effects ");
            output.WriteLine("{");
            output.Indent();
            output.WriteLine("using namespace ::Microsoft::WRL;");
            output.WriteLine("using namespace ABI::Microsoft::Graphics::Canvas;");
            output.WriteLine();
            output.WriteLine("class " + effect.ClassName + " : public RuntimeClass<");
            output.Indent();
            output.WriteLine(effect.InterfaceName + ",");
            output.WriteLine("MixIn<" + effect.ClassName + ", CanvasEffect>>,");
            output.WriteLine("public CanvasEffect");
            output.Unindent();
            output.WriteLine("{");
            output.Indent();
            output.WriteLine("InspectableClass(RuntimeClass_Microsoft_Graphics_Canvas_Effects_" + effect.ClassName + ", BaseTrust);");
            output.WriteLine();
            output.Unindent();
            output.WriteLine("public:");
            output.Indent();
            output.WriteLine(effect.ClassName + "();");
            output.WriteLine();

            foreach (var property in effect.Properties)
            {
                // Property with type string describes 
                // name/author/category/description of effect but not input type
                if (property.Type == "string" || property.IsHidden)
                    continue;

                output.WriteLine("PROPERTY(" + property.Name + ", " + property.TypeNameCpp + ");");
            }

            if (!(effect.Inputs.Maximum != null && effect.Inputs.Maximum == "0xFFFFFFFF"))
            {
                for (int i = 0; i < effect.Inputs.InputsList.Count; ++i)
                {
                    var input = effect.Inputs.InputsList[i];
                    output.WriteLine("PROPERTY(" + input.Name + ", IEffectInput*);");
                }
            }
            output.Unindent();
            output.WriteLine("};");
            output.Unindent();
            output.WriteLine("}}}}}");
        }

        public static void OutputEffectCpp(Effect effect, Formatter output)
        {
            OutputDataTypes.OutputLeadingComment(output);

            bool isInputSizeFixed = true;
            if (effect.Inputs.Maximum != null && effect.Inputs.Maximum == "0xFFFFFFFF")
                isInputSizeFixed = false;

            output.WriteLine("#include \"pch.h\"");
            output.WriteLine("#include \"" + effect.ClassName + ".h\"");
            output.WriteLine();
            output.WriteLine("namespace ABI { namespace Microsoft { namespace Graphics { namespace Canvas { namespace Effects");
            output.WriteLine("{");
            output.Indent();
            output.WriteLine(effect.ClassName + "::" + effect.ClassName + "()");
            output.WriteIndent();
            int inputsCount = effect.Inputs.InputsList.Count;
            if (effect.Inputs.Maximum != null && effect.Inputs.Maximum == "0xFFFFFFFF")
                inputsCount = 0;
            output.WriteLine(": CanvasEffect(CLSID_D2D1"
                             + EffectGenerator.FormatClassName(effect.Properties[0].Value) + ", "
                             + (effect.Properties.Count(p => !p.IsHandCoded) - 4) + ", "
                             + inputsCount + ", "
                             + isInputSizeFixed.ToString().ToLower() + ")");
            output.WriteLine("{");
            output.Indent();
            output.WriteLine("// Set default values");

            foreach (var property in effect.Properties)
            {
                WritePropertyInitialization(output, property);
            }
            output.Unindent();
            output.WriteLine("}");
            output.WriteLine();
            foreach (var property in effect.Properties)
            {
                WritePropertyImplementation(effect, output, property);
            }

            if (!(effect.Inputs.Maximum != null && effect.Inputs.Maximum == "0xFFFFFFFF"))
            {
                for (int i = 0; i < effect.Inputs.InputsList.Count; ++i)
                {
                    var input = effect.Inputs.InputsList[i];
                    output.WriteLine("IMPLEMENT_INPUT_PROPERTY(" + effect.ClassName + ",");
                    output.Indent();
                    output.WriteLine(input.Name + ",");
                    output.WriteLine(i.ToString() + ")");
                    output.Unindent();
                    output.WriteLine();
                }
            }

            output.WriteLine("ActivatableClass(" + effect.ClassName + ");");

            output.Unindent();
            output.WriteLine("}}}}}");
        }

        private static void WritePropertyInitialization(Formatter output, Property property)
        {
            // Property with type string describes 
            // name/author/category/description of effect but not input type
            if (property.Type == "string" || property.IsHandCoded)
                return;

            string defaultValue = property.Properties.Find(internalProperty => internalProperty.Name == "Default").Value;

            output.WriteLine("SetProperty<" + property.TypeNameBoxed + ">(" + property.NativePropertyName + ", " + FormatPropertyValue(property, defaultValue) + ");");
        }

        private static void WritePropertyImplementation(Effect effect, Formatter output, Property property)
        {
            // Property with type string describes 
            // name/author/category/description of effect but not input type
            if (property.Type == "string" || property.IsHandCoded || property.IsHidden)
                return;

            var min = property.Properties.Find(internalProperty => internalProperty.Name == "Min");
            var max = property.Properties.Find(internalProperty => internalProperty.Name == "Max");

            bool isWithUnsupported = (property.ExcludedEnumIndexes != null) && (property.ExcludedEnumIndexes.Count != 0);

            bool isValidation = (min != null) || (max != null) || isWithUnsupported;

            if (isValidation)
            {
                output.WriteLine("IMPLEMENT_PROPERTY_WITH_VALIDATION(" + effect.ClassName + ",");
            }
            else
            {
                output.WriteLine("IMPLEMENT_PROPERTY(" + effect.ClassName + ",");
            }

            output.Indent();
            output.WriteLine(property.Name + ",");
            output.WriteLine(property.TypeNameBoxed + ",");
            output.WriteLine(property.TypeNameCpp + ",");

            if (isValidation)
            {
                output.WriteLine(property.NativePropertyName + ",");

                var validationChecks = new List<string>();

                if (min != null)
                {
                    AddValidationChecks(validationChecks, property, min.Value, ">=");
                }

                if (max != null)
                {
                    AddValidationChecks(validationChecks, property, max.Value, "<=");
                }

                if (isWithUnsupported)
                {
                    foreach (var index in property.ExcludedEnumIndexes)
                    {
                        validationChecks.Add("value != static_cast<" + property.TypeNameCpp + ">(" + index + ")");
                    }
                }

                output.WriteLine("(" + string.Join(") && (", validationChecks) + "))");
            }
            else
            {
                output.WriteLine(property.NativePropertyName + ")");
            }

            output.Unindent();
            output.WriteLine();
        }

        static void AddValidationChecks(List<string> validationChecks, Property property, string minOrMax, string comparisonOperator)
        {
            if (property.Type.StartsWith("vector"))
            {
                // Expand out a separate "value.X >= limit" check for each component of the vector.
                int componentIndex = 0;

                foreach (var componentMinOrMax in SplitVectorValue(minOrMax))
                {
                    var componentName = "XYZW"[componentIndex++];

                    validationChecks.Add("value." + componentName + " " + comparisonOperator + " " + componentMinOrMax);
                }
            }
            else
            {
                // Simple "value >= limit" check.
                validationChecks.Add("value " + comparisonOperator + " " + FormatPropertyValue(property, minOrMax));
            }
        }

        static string FormatPropertyValue(Property property, string value)
        {
            if (property.Type == "enum")
            {
                if (property.EnumFields.NativeEnum != null)
                {
                    value = property.EnumFields.NativeEnum.Enums[Int32.Parse(value)];
                }
                else
                {
                    value = property.TypeNameCpp + "::" + property.EnumFields.FieldsList[Int32.Parse(value)].Name;
                }
            }
            else if (property.Type.StartsWith("matrix") || property.Type.StartsWith("vector"))
            {
                var values = SplitVectorValue(value);

                if (property.TypeNameCpp == "Color")
                {
                    values = ConvertVectorToColor(values);
                }
                else if (property.TypeNameCpp == "Rect")
                {
                    values = ConvertVectorToRect(values);
                }

                value = property.TypeNameCpp + "{ " + string.Join(", ", values) + " }";
            }
            else if (property.Type == "float")
            {
                if (!value.Contains('.'))
                {
                    value += ".0";
                }

                value += "f";
            }
            else if (property.Type == "uint32")
            {
                value += "u";
            }
            else if (property.Type == "bool")
            {
                value = "static_cast<boolean>(" + value + ")";
            }

            return value;
        }

        static IEnumerable<string> SplitVectorValue(string value)
        {
            // Convert "( 1.0, 2.0, 3 )" to "1.0f", "2.0f", "3"
            return value.TrimStart('(')
                        .TrimEnd(')')
                        .Replace(" ", "")
                        .Replace("inf", stdInfinity)
                        .Split(',')
                        .Select(v => v.Contains('.') ? v + 'f' : v);
        }

        static IEnumerable<string> ConvertVectorToColor(IEnumerable<string> values)
        {
            // Convert 0-1 range to 0-255.
            var colorValues = from value in values
                              select StringToFloat(value) * 255;

            // Add an alpha component if the input was a 3-component vector.
            if (colorValues.Count() < 4)
            {
                colorValues = colorValues.Concat(new float[] { 255 });
            }

            // Rearrange RGBA to ARGB.
            var rgb = colorValues.Take(3);
            var a = colorValues.Skip(3);

            return from value in a.Concat(rgb)
                   select value.ToString();
        }

        static IEnumerable<string> ConvertVectorToRect(IEnumerable<string> values)
        {
            // This is in left/top/right/bottom format.
            var rectValues = values.Select(StringToFloat).ToArray();

            // Convert to x/y/w/h.
            rectValues[2] -= rectValues[0];
            rectValues[3] -= rectValues[1];

            return from value in rectValues
                   select value.ToString().Replace("Infinity", stdInfinity);
        }

        static float StringToFloat(string value)
        {
            if (value == stdInfinity)
            {
                return float.PositiveInfinity;
            }
            else if (value == "-" + stdInfinity)
            {
                return float.NegativeInfinity;
            }
            else
            {
                return float.Parse(value.Replace("f", ""));
            }
        }
    }
}
