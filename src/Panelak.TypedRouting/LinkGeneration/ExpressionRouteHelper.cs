namespace Panelak.TypedRouting
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Abstractions;
    using Microsoft.AspNetCore.Mvc.Controllers;
    using Microsoft.AspNetCore.Mvc.Infrastructure;
    using Microsoft.AspNetCore.Routing;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Threading.Tasks;

    public class ExpressionRouteHelper : IExpressionRouteHelper
    {
        // This key should be ignored as it is used internally for route attribute matching.
        private const string RouteGroupKey = "!__route_group";

        private readonly ConcurrentDictionary<MethodInfo, ControllerActionDescriptor> controllerActionDescriptorCache;
        private readonly IActionDescriptorCollectionProvider actionDescriptorsCollection;
        private readonly ISet<string> uniqueRouteKeys;

        public ExpressionRouteHelper(
            IActionDescriptorCollectionProvider actionDescriptorsCollectionProvider)
        {
            controllerActionDescriptorCache = new ConcurrentDictionary<MethodInfo, ControllerActionDescriptor>();

            uniqueRouteKeys = new HashSet<string>();
            actionDescriptorsCollection = actionDescriptorsCollectionProvider;
        }

        public ExpressionRouteValues Resolve<TController>(
            Expression<Action<TController>> expression,
            object additionalRouteValues = null,
            bool addControllerAndActionToRouteValues = false)
        {
            return ResolveLambdaExpression(
                expression,
                additionalRouteValues,
                addControllerAndActionToRouteValues);
        }

        public ExpressionRouteValues Resolve<TController>(
            Expression<Func<TController, Task>> expression,
            object additionalRouteValues = null,
            bool addControllerAndActionToRouteValues = false)
        {
            return ResolveLambdaExpression(
                expression,
                additionalRouteValues,
                addControllerAndActionToRouteValues);
        }

        public void ClearActionCache()
        {
            controllerActionDescriptorCache.Clear();
        }

        private ExpressionRouteValues ResolveLambdaExpression(
            LambdaExpression expression,
            object additionalRouteValues,
            bool addControllerAndActionToRouteValues)
        {
            if (expression == null)
            {
                throw new ArgumentNullException(nameof(expression));
            }

            if (expression.Body is MethodCallExpression methodCallExpression)
            {
                Type controllerType = methodCallExpression.Object?.Type;
                if (controllerType == null)
                {
                    // Method call is not valid because it is static.
                    throw new InvalidOperationException("Expression is not valid - expected instance method call but instead received static method call.");
                }

                MethodInfo methodInfo = methodCallExpression.Method;

                // Find controller action descriptor from the provider with the same extracted method info.
                // This search is potentially slow, so it is cached after the first lookup.
                ControllerActionDescriptor controllerActionDescriptor = GetActionDescriptorFromCache(methodInfo);

                string controllerName = controllerActionDescriptor.ControllerName;
                string actionName = controllerActionDescriptor.ActionName;

                IDictionary<string, object> routeValues = GetRouteValues(methodInfo, methodCallExpression, controllerActionDescriptor);

                // If there is a required route value, add it to the result.
                foreach (KeyValuePair<string, string> requiredRouteValue in controllerActionDescriptor.RouteValues)
                {
                    string routeKey = requiredRouteValue.Key;
                    string routeValue = requiredRouteValue.Value;

                    if (String.Equals(routeKey, RouteGroupKey))
                    {
                        continue;
                    }

                    if (routeValue != String.Empty)
                    {
                        // Override the 'default' values.
                        if (String.Equals(routeKey, "controller", StringComparison.OrdinalIgnoreCase))
                        {
                            controllerName = routeValue;
                        }
                        else if (String.Equals(routeKey, "action", StringComparison.OrdinalIgnoreCase))
                        {
                            actionName = routeValue;
                        }
                        else
                        {
                            routeValues[routeKey] = routeValue;
                        }
                    }
                }

                ApplyAdditionaRouteValues(additionalRouteValues, routeValues);

                if (addControllerAndActionToRouteValues)
                {
                    AddControllerAndActionToRouteValues(controllerName, actionName, routeValues);
                }

                foreach (string uniqueRouteKey in uniqueRouteKeys)
                {
                    if (!routeValues.ContainsKey(uniqueRouteKey))
                    {
                        routeValues.Add(uniqueRouteKey, String.Empty);
                    }
                }

                return new ExpressionRouteValues
                {
                    Controller = controllerName,
                    Action = actionName,
                    RouteValues = routeValues
                };
            }

            // Expression is invalid because it is not a method call.
            throw new InvalidOperationException("Expression is not valid - expected instance method call but instead received other type of expression.");
        }

        private ControllerActionDescriptor GetActionDescriptorFromCache(MethodInfo methodInfo)
        {
            return controllerActionDescriptorCache.GetOrAdd(methodInfo, _ =>
            {
                // we are only interested in controller actions
                ControllerActionDescriptor foundControllerActionDescriptor = null;
                IReadOnlyList<Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor> actionDescriptors = actionDescriptorsCollection.ActionDescriptors.Items;
                for (int i = 0; i < actionDescriptors.Count; i++)
                {
                    Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor actionDescriptor = actionDescriptors[i];
                    if (actionDescriptor is ControllerActionDescriptor descriptor && descriptor.MethodInfo == methodInfo)
                    {
                        foundControllerActionDescriptor = descriptor;
                        break;
                    }
                }

                if (foundControllerActionDescriptor == null)
                {
                    throw new InvalidOperationException($"Method {methodInfo.Name} in class {methodInfo.DeclaringType.Name} is not a valid controller action.");
                }

                return foundControllerActionDescriptor;
            });
        }

        private IDictionary<string, object> GetRouteValues(
            MethodInfo methodInfo,
            MethodCallExpression methodCallExpression,
            ControllerActionDescriptor controllerActionDescriptor)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            ReadOnlyCollection<Expression> arguments = methodCallExpression.Arguments;
            if (arguments.Count == 0)
            {
                return result;
            }

            ParameterInfo[] methodParameterNames = methodInfo.GetParameters();

            var parameterDescriptors = new Dictionary<string, string>();
            IList<ParameterDescriptor> parameters = controllerActionDescriptor.Parameters;
            for (int i = 0; i < parameters.Count; i++)
            {
                Microsoft.AspNetCore.Mvc.Abstractions.ParameterDescriptor parameter = parameters[i];
                if (parameter.BindingInfo != null)
                {
                    parameterDescriptors.Add(parameter.Name, parameter.BindingInfo.BinderModelName);
                }
            }

            for (int i = 0; i < arguments.Count; i++)
            {
                string methodParameterName = methodParameterNames[i].Name;
                if (parameterDescriptors.ContainsKey(methodParameterName))
                {
                    methodParameterName = parameterDescriptors[methodParameterName] ?? methodParameterName;
                }

                Expression expressionArgument = arguments[i];

                if (expressionArgument.NodeType == ExpressionType.Convert)
                {
                    // Expression which contains converting from type to type
                    var expressionArgumentAsUnary = (UnaryExpression)expressionArgument;
                    expressionArgument = expressionArgumentAsUnary.Operand;
                }

                if (expressionArgument.NodeType == ExpressionType.Call)
                {
                    // Expression of type c => c.Action(With.No<int>()) - value should be ignored and can be skipped.
                    var expressionArgumentAsMethodCall = (MethodCallExpression)expressionArgument;
                    if (expressionArgumentAsMethodCall.Object == null
                        && expressionArgumentAsMethodCall.Method.DeclaringType == typeof(With))
                    {
                        continue;
                    }
                }

                object value = null;
                if (expressionArgument.NodeType == ExpressionType.Constant)
                {
                    // Expression of type c => c.Action({const}) - value can be extracted without compiling.
                    value = ((ConstantExpression)expressionArgument).Value;
                }
                else if (expressionArgument.NodeType == ExpressionType.MemberAccess
                    && ((MemberExpression)expressionArgument).Member is FieldInfo)
                {
                    // Expression of type c => c.Action(id)
                    // Value can be extracted without compiling.
                    var memberAccessExpr = (MemberExpression)expressionArgument;
                    var constantExpression = (ConstantExpression)memberAccessExpr.Expression;
                    if (constantExpression != null)
                    {
                        string innerMemberName = memberAccessExpr.Member.Name;
                        FieldInfo compiledLambdaScopeField = constantExpression.Value.GetType().GetField(innerMemberName);
                        value = compiledLambdaScopeField.GetValue(constantExpression.Value);
                    }
                }
                else
                {
                    // Expresion needs compiling because it is not of constant type.
                    UnaryExpression convertExpression = Expression.Convert(expressionArgument, typeof(object));
                    value = Expression.Lambda<Func<object>>(convertExpression).Compile().Invoke();
                }

                // We are interested only in not null route values.
                if (value != null)
                {
                    result[methodParameterName] = value;
                }
            }

            return result;
        }

        private static void ApplyAdditionaRouteValues(object routeValues, IDictionary<string, object> result)
        {
            if (routeValues != null)
            {
                var additionalRouteValues = new RouteValueDictionary(routeValues);

                foreach (KeyValuePair<string, object> additionalRouteValue in additionalRouteValues)
                {
                    result[additionalRouteValue.Key] = additionalRouteValue.Value;
                }
            }
        }

        private static void AddControllerAndActionToRouteValues(string controllerName, string actionName, IDictionary<string, object> routeValues)
        {
            routeValues["controller"] = controllerName;
            routeValues["action"] = actionName;
        }
    }
}
