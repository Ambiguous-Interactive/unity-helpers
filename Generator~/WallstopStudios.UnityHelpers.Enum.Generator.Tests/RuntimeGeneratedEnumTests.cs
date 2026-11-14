// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Enum.Generator.Tests
{
    using System;
    using System.Reflection;
    using System.Reflection.Emit;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    public sealed class RuntimeGeneratedEnumTests
    {
        private static Type CreateEnum(
            string name,
            Type underlyingType,
            bool flags,
            params int[] values
        )
        {
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("EnumGeneratorDynamic" + Guid.NewGuid().ToString("N")),
                AssemblyBuilderAccess.Run
            );
            ModuleBuilder module = assembly.DefineDynamicModule("Dynamic");
            EnumBuilder builder = module.DefineEnum(
                name + Guid.NewGuid().ToString("N"),
                TypeAttributes.Public,
                underlyingType
            );
            if (flags)
            {
                builder.SetCustomAttribute(
                    new CustomAttributeBuilder(
                        typeof(FlagsAttribute).GetConstructor(Type.EmptyTypes),
                        Array.Empty<object>()
                    )
                );
            }
            for (int index = 0; index < values.Length; ++index)
            {
                builder.DefineLiteral("V" + index, values[index]);
            }
            return builder.CreateType();
        }

        private static object[] ToObjects(Array values)
        {
            object[] result = new object[values.Length];
            for (int index = 0; index < values.Length; ++index)
            {
                result[index] = values.GetValue(index);
            }
            return result;
        }

        [Test]
        public void RuntimeGeneratedEnumUsesTheGeneralizedPath()
        {
            Type enumType = CreateEnum("IntDynamic", typeof(int), false, 0, 1, 2, 4, 7);
            object[] values = Enum.GetValues(enumType) is Array array
                ? ToObjects(array)
                : Array.Empty<object>();
            Assert.That(values.Length, Is.EqualTo(5));
            Assert.That(EnumValues.Of(enumType).Length, Is.EqualTo(5));

            Type valuesType = typeof(EnumValues<>).MakeGenericType(enumType);
            MethodInfo isDefined = valuesType.GetMethod("IsDefined");
            Type nameType = typeof(EnumNameCache<>).MakeGenericType(enumType);
            MethodInfo toName = nameType.GetMethod("ToCachedName");
            Assert.That(isDefined, Is.Not.Null);
            Assert.That(toName, Is.Not.Null);

            foreach (object value in values)
            {
                Assert.That(
                    isDefined.Invoke(null, new[] { value }),
                    Is.EqualTo(Enum.IsDefined(enumType, value)),
                    value.ToString()
                );
                Assert.That(toName.Invoke(null, new[] { value }), Is.EqualTo(value.ToString()));
                Enum boxed = (Enum)value;
                Assert.That(boxed.TryConvertToUInt64(out _), Is.True);
            }
        }

        [Test]
        public void RuntimeGeneratedFlagsEnumUsesTheGeneralizedFlagCheck()
        {
            Type enumType = CreateEnum("FlagsDynamic", typeof(int), true, 0, 1, 2, 4, 7);
            object[] values = ToObjects((Array)Enum.GetValues(enumType));
            MethodInfo hasFlag = typeof(EnumExtensions)
                .GetMethod("HasFlagNoAlloc")
                .MakeGenericMethod(enumType);

            foreach (object value in values)
            {
                foreach (object flag in values)
                {
                    Assert.That(
                        hasFlag.Invoke(null, new[] { value, flag }),
                        Is.EqualTo(((Enum)value).HasFlag((Enum)flag)),
                        value + " / " + flag
                    );
                }
            }
        }

        [TestCase(typeof(char))]
        [TestCase(typeof(bool))]
        public void UnsupportedUnderlyingTypesFallBackToTheRuntime(Type underlyingType)
        {
            Type enumType;
            try
            {
                enumType = CreateEnum(
                    "Unsupported" + underlyingType.Name,
                    underlyingType,
                    false,
                    0,
                    1
                );
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    || exception is TypeLoadException
                    || exception is NotSupportedException
                )
            {
                Assert.Ignore(
                    "This runtime cannot construct an enum with underlying type "
                        + underlyingType.Name
                );
                return;
            }

            Type valuesType = typeof(EnumValues<>).MakeGenericType(enumType);
            MethodInfo isDefined = valuesType.GetMethod("IsDefined");
            object[] values = ToObjects((Array)Enum.GetValues(enumType));
            foreach (object value in values)
            {
                Assert.That(
                    isDefined.Invoke(null, new[] { value }),
                    Is.EqualTo(Enum.IsDefined(enumType, value)),
                    value.ToString()
                );
            }
        }
    }
}
