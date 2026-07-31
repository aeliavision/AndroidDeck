using System;

namespace VcfEditor.Services
{
    public interface IApplicationExceptionHandler
    {
        /// <summary>
        /// Logs and presents a safe user-facing error. Returns true only when execution may continue.
        /// </summary>
        bool Handle(Exception exception);
    }
}
