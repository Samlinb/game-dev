import { Button, Node } from 'cc';

export interface UiWorkflowAction {
    nodeId: string;
    action: string;
}

export function bindUiWorkflowActions(
    root: Node,
    actions: ReadonlyArray<UiWorkflowAction>,
    dispatch: (action: string) => void,
): () => void {
    const buttons = new Map(root.getComponentsInChildren(Button).map((button) => [button.node.name, button]));
    const bindings = actions.map(({ nodeId, action }) => {
        const button = buttons.get(nodeId);
        if (!button) throw new Error(`UI Workflow Button not found: ${nodeId}`);
        return { node: button.node, listener: () => dispatch(action) };
    });
    for (const { node, listener } of bindings) node.on(Button.EventType.CLICK, listener);
    return () => {
        for (const { node, listener } of bindings) node.off(Button.EventType.CLICK, listener);
    };
}
