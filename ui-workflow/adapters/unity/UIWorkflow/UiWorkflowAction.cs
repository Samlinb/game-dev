using UnityEngine;
using UnityEngine.Events;

namespace UIWorkflow
{
    public sealed class UiWorkflowAction : MonoBehaviour
    {
        public string actionId;
        public UnityEvent invoked = new UnityEvent();

        public void Dispatch()
        {
            invoked.Invoke();
        }
    }
}
