namespace Panelak.TypedRouting
{
    using Microsoft.Extensions.DependencyInjection;
    using Panelak.TypedRouting;
    using System;

    internal static class ServiceProviderExtensions
    {
        public static IExpressionRouteHelper GetExpressionRouteHelper(this IServiceProvider serviceProvider)
        {
            IExpressionRouteHelper expressionRouteHelper = serviceProvider?.GetService<IExpressionRouteHelper>();
            if (expressionRouteHelper == null)
            {
                throw new InvalidOperationException("'AddTypedRouting' must be called after 'AddMvc' in order to use typed routing and link generation.");
            }

            return expressionRouteHelper;
        }
    }
}