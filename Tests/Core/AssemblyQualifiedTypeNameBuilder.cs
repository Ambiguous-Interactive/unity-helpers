// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Text;

    internal static class AssemblyQualifiedTypeNameBuilder
    {
        internal static string Build(Type type)
        {
            StringBuilder builder = new();
            AppendTypeName(builder, type);
            builder.Append(", ").Append(type.Assembly.FullName);
            return builder.ToString();
        }

        private static void AppendTypeName(StringBuilder builder, Type type)
        {
            if (type.IsArray)
            {
                AppendTypeName(builder, type.GetElementType());
                builder.Append('[');
                int arrayRank = type.GetArrayRank();
                if (arrayRank == 1 && !type.IsSZArray)
                {
                    builder.Append('*');
                }

                for (int rank = 1; rank < arrayRank; rank++)
                {
                    builder.Append(',');
                }

                builder.Append(']');
                return;
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                builder.Append(type.GetGenericTypeDefinition().FullName).Append('[');
                Type[] arguments = type.GetGenericArguments();
                for (int index = 0; index < arguments.Length; index++)
                {
                    if (0 < index)
                    {
                        builder.Append(',');
                    }

                    builder.Append('[').Append(Build(arguments[index])).Append(']');
                }

                builder.Append(']');
                return;
            }

            builder.Append(type.FullName);
        }
    }
}
