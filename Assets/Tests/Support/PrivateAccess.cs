/// <summary>Provides the PrivateAccess helper that reaches the non-public members of the code under test.</summary>
using System;
using System.Reflection;

namespace SL.Tests
{
    /// <summary>Reads, writes, and invokes the non-public members of a type under test through reflection.</summary>
    /// <remarks>
    /// Unity invokes lifecycle callbacks such as Awake, Start, Update, OnTriggerEnter, and OnTriggerExit on private
    /// methods, and Edit Mode tests must drive those transitions themselves because no Unity player loop runs.
    /// </remarks>
    public static class PrivateAccess
    {
        /// <summary>
        /// The binding flags matching any instance member the searched type itself declares, regardless of its access
        /// modifier, with inherited members reached by the base-chain walk in FindMethod, FindField, and FindProperty.
        /// </summary>
        private const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>
        /// The binding flags matching any static member the searched type itself declares, regardless of its access
        /// modifier, with inherited members reached by the base-chain walk in FindMethod, FindField, and FindProperty.
        /// </summary>
        private const BindingFlags StaticFlags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>Invokes an instance method by name, including a private Unity lifecycle callback.</summary>
        /// <param name="target">The instance whose method to invoke.</param>
        /// <param name="methodName">The method name to resolve on the instance type or one of its base types.</param>
        /// <param name="arguments">The positional arguments forwarded to the resolved method.</param>
        /// <returns>The method's return value, or null for a void method.</returns>
        /// <exception cref="ArgumentNullException">The target instance is null.</exception>
        /// <exception cref="MissingMethodException">No such method exists on the type or its base types.</exception>
        public static object Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = FindMethod(RequireTarget(target, methodName), methodName, InstanceFlags);
            return InvokeResolved(method, target, arguments);
        }

        /// <summary>Invokes a static method by name on the specified type.</summary>
        /// <param name="type">The type declaring the static method.</param>
        /// <param name="methodName">The method name to resolve on the type or one of its base types.</param>
        /// <param name="arguments">The positional arguments forwarded to the resolved method.</param>
        /// <returns>The method's return value, or null for a void method.</returns>
        /// <exception cref="MissingMethodException">No such method exists on the type or its base types.</exception>
        public static object InvokeStatic(Type type, string methodName, params object[] arguments)
        {
            MethodInfo method = FindMethod(type, methodName, StaticFlags);
            return InvokeResolved(method, null, arguments);
        }

        /// <summary>Reads the value of an instance field by name.</summary>
        /// <typeparam name="TValue">The type the field value is cast to.</typeparam>
        /// <param name="target">The instance whose field to read.</param>
        /// <param name="fieldName">The field name to resolve on the instance type or one of its base types.</param>
        /// <returns>The current field value.</returns>
        /// <exception cref="ArgumentNullException">The target instance is null.</exception>
        /// <exception cref="MissingFieldException">No such field exists on the type or its base types.</exception>
        public static TValue GetField<TValue>(object target, string fieldName)
        {
            FieldInfo field = FindField(RequireTarget(target, fieldName), fieldName, InstanceFlags);
            return (TValue)field.GetValue(target);
        }

        /// <summary>Writes the value of an instance field by name.</summary>
        /// <param name="target">The instance whose field to write.</param>
        /// <param name="fieldName">The field name to resolve on the instance type or one of its base types.</param>
        /// <param name="value">The value assigned to the resolved field.</param>
        /// <exception cref="ArgumentNullException">The target instance is null.</exception>
        /// <exception cref="MissingFieldException">No such field exists on the type or its base types.</exception>
        public static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = FindField(RequireTarget(target, fieldName), fieldName, InstanceFlags);
            field.SetValue(target, value);
        }

        /// <summary>Reads the value of a static field by name.</summary>
        /// <typeparam name="TValue">The type the field value is cast to.</typeparam>
        /// <param name="type">The type declaring the static field.</param>
        /// <param name="fieldName">The field name to resolve on the type or one of its base types.</param>
        /// <returns>The current field value.</returns>
        /// <exception cref="MissingFieldException">No such field exists on the type or its base types.</exception>
        public static TValue GetStaticField<TValue>(Type type, string fieldName)
        {
            FieldInfo field = FindField(type, fieldName, StaticFlags);
            return (TValue)field.GetValue(null);
        }

        /// <summary>Writes the value of a static field by name.</summary>
        /// <param name="type">The type declaring the static field.</param>
        /// <param name="fieldName">The field name to resolve on the type or one of its base types.</param>
        /// <param name="value">The value assigned to the resolved field.</param>
        /// <exception cref="MissingFieldException">No such field exists on the type or its base types.</exception>
        public static void SetStaticField(Type type, string fieldName, object value)
        {
            FieldInfo field = FindField(type, fieldName, StaticFlags);
            field.SetValue(null, value);
        }

        /// <summary>Writes a static property that exposes a non-public setter, such as a singleton handle.</summary>
        /// <param name="type">The type declaring the static property.</param>
        /// <param name="propertyName">The property name to resolve on the type or one of its base types.</param>
        /// <param name="value">The value assigned to the resolved property.</param>
        /// <exception cref="MissingMemberException">The property is missing, or it exposes a getter only.</exception>
        public static void SetStaticProperty(Type type, string propertyName, object value)
        {
            PropertyInfo property = FindProperty(type, propertyName, StaticFlags);
            MethodInfo setter = property.GetSetMethod(nonPublic: true);
            if (setter == null)
            {
                string message =
                    $"Unable to write static property '{propertyName}' on type '{type.FullName}'. The property must "
                    + "declare a setter, but it exposes a getter only.";
                throw new MissingMemberException(message);
            }
            setter.Invoke(null, new[] { value });
        }

        /// <summary>Returns the declaring type of the target instance, rejecting a null target.</summary>
        /// <param name="target">The instance whose type to resolve.</param>
        /// <param name="memberName">The member name being resolved, quoted in the failure message.</param>
        /// <returns>The runtime type of the target instance.</returns>
        /// <exception cref="ArgumentNullException">The target instance is null.</exception>
        private static Type RequireTarget(object target, string memberName)
        {
            if (target == null)
            {
                string message =
                    $"Unable to resolve member '{memberName}'. The target instance must be non-null, but it is null.";
                throw new ArgumentNullException(nameof(target), message);
            }
            return target.GetType();
        }

        /// <summary>Resolves a method by walking the type's base chain.</summary>
        /// <param name="type">The type to search, followed by each of its base types.</param>
        /// <param name="methodName">The method name to resolve.</param>
        /// <param name="flags">The binding flags selecting the instance or static member set.</param>
        /// <returns>The resolved method.</returns>
        /// <exception cref="MissingMethodException">No such method exists on the type or its base types.</exception>
        private static MethodInfo FindMethod(Type type, string methodName, BindingFlags flags)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(methodName, flags);
                if (method != null)
                {
                    return method;
                }
            }

            string message =
                $"Unable to resolve method '{methodName}'. The name must match a method declared by "
                + $"'{type.FullName}' or one of its base types, but no such method exists.";
            throw new MissingMethodException(message);
        }

        /// <summary>Resolves a field by walking the type's base chain.</summary>
        /// <param name="type">The type to search, followed by each of its base types.</param>
        /// <param name="fieldName">The field name to resolve.</param>
        /// <param name="flags">The binding flags selecting the instance or static member set.</param>
        /// <returns>The resolved field.</returns>
        /// <exception cref="MissingFieldException">No such field exists on the type or its base types.</exception>
        private static FieldInfo FindField(Type type, string fieldName, BindingFlags flags)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(fieldName, flags);
                if (field != null)
                {
                    return field;
                }
            }

            string message =
                $"Unable to resolve field '{fieldName}'. The name must match a field declared by "
                + $"'{type.FullName}' or one of its base types, but no such field exists.";
            throw new MissingFieldException(message);
        }

        /// <summary>Resolves a property by walking the type's base chain.</summary>
        /// <param name="type">The type to search, followed by each of its base types.</param>
        /// <param name="propertyName">The property name to resolve.</param>
        /// <param name="flags">The binding flags selecting the instance or static member set.</param>
        /// <returns>The resolved property.</returns>
        /// <exception cref="MissingMemberException">No such property exists on the type or its base types.</exception>
        private static PropertyInfo FindProperty(Type type, string propertyName, BindingFlags flags)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(propertyName, flags);
                if (property != null)
                {
                    return property;
                }
            }

            string message =
                $"Unable to resolve property '{propertyName}'. The name must match a property declared by "
                + $"'{type.FullName}' or one of its base types, but no such property exists.";
            throw new MissingMemberException(message);
        }

        /// <summary>Invokes a resolved method and unwraps the reflection exception wrapper.</summary>
        /// <remarks>
        /// Reflection wraps anything the target throws in a <see cref="TargetInvocationException"/>, which would hide
        /// the exception type a test asserts on, so the inner exception is rethrown with its stack trace preserved.
        /// </remarks>
        /// <param name="method">The resolved method to invoke.</param>
        /// <param name="target">The instance to invoke against, or null for a static method.</param>
        /// <param name="arguments">The positional arguments forwarded to the method.</param>
        /// <returns>The method's return value, or null for a void method.</returns>
        private static object InvokeResolved(MethodInfo method, object target, object[] arguments)
        {
            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
    }
}
