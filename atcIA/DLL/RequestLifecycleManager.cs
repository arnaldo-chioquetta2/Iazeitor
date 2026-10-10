using System.Collections.Generic;

namespace GptBolDll
{
    public sealed class RequestLifecycleManager
    {
        private readonly Dictionary<RequestContext, RequestState> _estados =
            new Dictionary<RequestContext, RequestState>();

        public void Iniciar(RequestContext context)
        {
            _estados[context] = RequestState.None;
        }

        public void AlterarEstado(RequestContext context, RequestState novoEstado)
        {
            _estados[context] = novoEstado;
        }

        public void Finalizar(RequestContext context)
        {
            _estados[context] = RequestState.Completed;
        }

        public RequestState ObterEstadoAtual(RequestContext context)
        {
            return _estados.TryGetValue(context, out RequestState estado) ? estado : RequestState.None;
        }
    }
}
