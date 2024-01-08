using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
public sealed class PerInjectAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class PerProvideAttribute : Attribute
{
}

public interface IDependencyProvider
{
}

[DefaultExecutionOrder(-1000)]
public class PerInjector : Singleton<PerInjector>
{
    const BindingFlags BINDING_FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    readonly Dictionary<Type, object> registry = new Dictionary<Type, object>();

    protected override void Awake()
    {
        base.Awake();

        var providers = FindMonoBehaviours().OfType<IDependencyProvider>();
        foreach (var provider in providers)
        {
            RegisterProvider(provider);
        }

        var injectables = FindMonoBehaviours().Where(IsInjectable);

        foreach (var injectable in injectables)
        {
            Inject(injectable);
        }
    }

    void Inject(object instancee)
    {
        var type = instancee.GetType();
        var fields = type.GetFields(BINDING_FLAGS)
            .Where(member => Attribute.IsDefined(member, typeof(PerInjectAttribute)));

        foreach (var injectableField in fields)
        {
            var fieldType = injectableField.FieldType;
            var resolvedInstance = Resolve(fieldType);

            if (resolvedInstance == null)
            {
                throw new Exception($"Failed to resolve {fieldType.Name} for {type.Name}");
            }

            injectableField.SetValue(instancee, resolvedInstance);
            Debug.Log($"Field Injected {fieldType.Name} into {type.Name}");
        }
        
        var methods = type.GetMethods(BINDING_FLAGS)
            .Where(member => Attribute.IsDefined(member, typeof(PerInjectAttribute)));

        foreach (var injectableMethod in methods)
        {
            var requiredParameters = injectableMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            var resolvedInstances = requiredParameters.Select(Resolve).ToArray();
            
            if (resolvedInstances.Any(resolvedInstance => resolvedInstance == null))
            {
                throw new Exception($"Failed to resolve parameters for {type.Name}.{injectableMethod.Name}");                
            }
            
            injectableMethod.Invoke(instancee, resolvedInstances);
            Debug.Log($"Method Injected {injectableMethod.Name} into {type.Name}");
        }
    }

    object Resolve(Type type)
    {
        registry.TryGetValue(type, out var resolvedInstance);
        return resolvedInstance;
    }

    static bool IsInjectable(MonoBehaviour obj)
    {
        return obj.GetType().GetFields(BINDING_FLAGS).Any(field => Attribute.IsDefined(field, typeof(PerInjectAttribute)));
    }

    void RegisterProvider(IDependencyProvider provider)
    {
        var methods = provider.GetType().GetMethods(BINDING_FLAGS);

        foreach (var method in methods)
        {
            if (!Attribute.IsDefined(method, typeof(PerProvideAttribute))) continue;

            var returnType = method.ReturnType;
            var providedInstance = method.Invoke(provider, null);

            if (providedInstance != null)
            {
                registry.Add(returnType, providedInstance);
                Debug.Log($"Registered {returnType.Name} from {provider.GetType().Name}");
            }
            else
            {
                throw new Exception($"Provider {provider.GetType().Name} returned null for {returnType.Name}");
            }
        }
    }

    static MonoBehaviour[] FindMonoBehaviours()
    {
        return FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.InstanceID);
    }
}