using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Polaris.Addons.Composition
{
    internal enum AddonServiceLifetime
    {
        Singleton,
        Transient,
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AddonModuleAttribute : Attribute { }

    public interface IAddonModule
    {
        void Configure(AddonServiceCollection services);
    }

    public sealed class AddonServiceCollection
    {
        private readonly Dictionary<Type, AddonServiceDescriptor> descriptors =
            new Dictionary<Type, AddonServiceDescriptor>();

        public void AddSingleton<TService>(TService instance) where TService : class
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            Add(new AddonServiceDescriptor(typeof(TService), instance.GetType(), AddonServiceLifetime.Singleton, instance));
        }

        public void AddSingleton<TService, TImplementation>()
            where TImplementation : class, TService =>
            Add(new AddonServiceDescriptor(
                typeof(TService), typeof(TImplementation), AddonServiceLifetime.Singleton, null));

        public void AddTransient<TService, TImplementation>()
            where TImplementation : class, TService =>
            Add(new AddonServiceDescriptor(
                typeof(TService), typeof(TImplementation), AddonServiceLifetime.Transient, null));

        private void Add(AddonServiceDescriptor descriptor)
        {
            if (descriptors.ContainsKey(descriptor.ServiceType))
            {
                throw new InvalidOperationException(
                    "Service '" + descriptor.ServiceType.FullName + "' is already registered.");
            }

            descriptors.Add(descriptor.ServiceType, descriptor);
        }

        internal AddonServiceProvider Build() => new AddonServiceProvider(descriptors.Values);
    }

    internal sealed class AddonServiceDescriptor
    {
        internal AddonServiceDescriptor(
            Type serviceType,
            Type implementationType,
            AddonServiceLifetime lifetime,
            object instance)
        {
            ServiceType = serviceType;
            ImplementationType = implementationType;
            Lifetime = lifetime;
            Instance = instance;
        }

        internal Type ServiceType { get; }

        internal Type ImplementationType { get; }

        internal AddonServiceLifetime Lifetime { get; }

        internal object Instance { get; set; }
    }

    internal sealed class AddonServiceProvider
    {
        private readonly Dictionary<Type, AddonServiceDescriptor> descriptors;
        private readonly object gate = new object();

        internal AddonServiceProvider(IEnumerable<AddonServiceDescriptor> descriptors)
        {
            this.descriptors = descriptors.ToDictionary(x => x.ServiceType);
        }

        internal object Create(Type implementationType)
        {
            if (implementationType == null)
            {
                throw new ArgumentNullException(nameof(implementationType));
            }

            return Resolve(implementationType, new Stack<Type>());
        }

        private object Resolve(Type requestedType, Stack<Type> chain)
        {
            if (chain.Contains(requestedType))
            {
                throw new InvalidOperationException(
                    "Circular Addons service dependency: " +
                    string.Join(" -> ", chain.Reverse().Select(x => x.Name)) + " -> " + requestedType.Name + ".");
            }

            if (!descriptors.TryGetValue(requestedType, out AddonServiceDescriptor descriptor))
            {
                if (requestedType.IsAbstract || requestedType.IsInterface)
                {
                    throw new InvalidOperationException(
                        "No Addons service is registered for '" + requestedType.FullName + "'.");
                }

                descriptor = new AddonServiceDescriptor(
                    requestedType,
                    requestedType,
                    AddonServiceLifetime.Transient,
                    null);
            }

            if (descriptor.Lifetime == AddonServiceLifetime.Singleton)
            {
                lock (gate)
                {
                    return descriptor.Instance ??= Construct(requestedType, descriptor.ImplementationType, chain);
                }
            }

            return Construct(requestedType, descriptor.ImplementationType, chain);
        }

        private object Construct(Type requestedType, Type implementationType, Stack<Type> chain)
        {
            chain.Push(requestedType);
            try
            {
                ConstructorInfo constructor = SelectConstructor(implementationType);
                return constructor.Invoke(constructor.GetParameters()
                    .Select(parameter => Resolve(parameter.ParameterType, chain))
                    .ToArray());
            }
            finally
            {
                chain.Pop();
            }
        }

        private static ConstructorInfo SelectConstructor(Type type)
        {
            ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (constructors.Length != 1)
            {
                throw new InvalidOperationException(
                    "Addons type '" + type.FullName + "' must have exactly one public constructor.");
            }

            return constructors[0];
        }
    }
}
