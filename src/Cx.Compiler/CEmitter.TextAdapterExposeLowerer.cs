using System.Text.RegularExpressions;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class TextAdapterExposeLowerer(
        CLoweringContext context,
        GenericCallResolver genericCallResolver,
        AdapterExposeResolver adapterExposeResolver)
    {
        public string LowerCalls(
            string expression,
            string variable,
            string receiver,
            string adapterName,
            IReadOnlyList<string> receiverArguments)
        {
            foreach (var expose in context.GetInstanceAdapterExposes(adapterName))
            {
                expression = LowerCall(expression, variable, receiver, expose, receiverArguments);
            }

            return expression;
        }

        public string LowerCall(
            string expression,
            string variable,
            string receiver,
            AdapterExposeInfo expose,
            IReadOnlyList<string> receiverArguments)
        {
            var resolvedExpose = adapterExposeResolver.Resolve(expose, receiverArguments);
            var genericBaseCall = genericCallResolver.FindExact(
                resolvedExpose.BaseOwner,
                resolvedExpose.SourceName,
                resolvedExpose.TypeArguments);

            var cName = genericBaseCall?.CName;
            if (cName is null)
            {
                if (!context.TryGetMethod($"{resolvedExpose.BaseOwner}.{resolvedExpose.SourceName}", out var baseMethod))
                {
                    return expression;
                }

                cName = baseMethod.CName;
            }

            expression = Regex.Replace(
                expression,
                $@"\b{Regex.Escape(variable)}\.{Regex.Escape(expose.ExposedName)}\(\s*\)",
                $"{cName}({receiver})");
            return Regex.Replace(
                expression,
                $@"\b{Regex.Escape(variable)}\.{Regex.Escape(expose.ExposedName)}\(",
                $"{cName}({receiver}, ");
        }
    }
}
