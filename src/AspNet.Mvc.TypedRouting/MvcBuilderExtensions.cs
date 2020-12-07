namespace Microsoft.AspNetCore.Builder
{
    using Microsoft.Extensions.DependencyInjection;
    using Panelak.TypedRouting;

    public static class MvcBuilderExtensions
    {
        /// <summary>
        /// Adds typed expression based routes in ASP.NET Core MVC application.
        /// </summary>
        /// <param name="routesConfiguration">Typed routes configuration.</param>
        public static IMvcBuilder AddTypedRouting(this IMvcBuilder mvcBuilder)
        {
            IServiceCollection services = mvcBuilder.Services;
            services.AddSingleton<IExpressionRouteHelper, ExpressionRouteHelper>();

            return mvcBuilder;
        }
    }
}
