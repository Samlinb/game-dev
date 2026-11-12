const { join } = require('node:path');

module.paths.push(join(Editor.App.path, 'node_modules'));
const drafts = new Map();

function align(cc, node, rect, anchor, parentSize) {
  const widget = node.addComponent(cc.Widget);
  widget.alignMode = cc.Widget.AlignMode.ON_WINDOW_RESIZE;
  widget.isAlignLeft = anchor === 'topLeft' || anchor === 'stretch';
  widget.isAlignRight = anchor === 'topRight' || anchor === 'stretch';
  widget.isAlignTop = anchor === 'topLeft' || anchor === 'topRight' || anchor === 'stretch';
  widget.isAlignBottom = anchor === 'bottomCenter' || anchor === 'stretch';
  widget.isAlignHorizontalCenter = anchor === 'center' || anchor === 'bottomCenter';
  widget.isAlignVerticalCenter = anchor === 'center';
  widget.left = rect.x;
  widget.right = parentSize.width - rect.x - rect.width;
  widget.top = rect.y;
  widget.bottom = parentSize.height - rect.y - rect.height;
  widget.horizontalCenter = rect.x + rect.width / 2 - parentSize.width / 2;
  widget.verticalCenter = parentSize.height / 2 - rect.y - rect.height / 2;
  widget.updateAlignment();
}

function createNode(cc, spec, parent, parentSize, frames, sliced) {
  const node = new cc.Node(spec.id);
  node.layer = cc.Layers.Enum.UI_2D;
  parent.addChild(node);
  const transform = node.addComponent(cc.UITransform);
  const rect = spec.rect;
  transform.setAnchorPoint(0, 1);
  transform.setContentSize(rect.width, rect.height);

  if (spec.type === 'image' || spec.type === 'button') {
    const sprite = node.addComponent(cc.Sprite);
    sprite.sizeMode = cc.Sprite.SizeMode.CUSTOM;
    sprite.type = sliced.has(spec.asset) ? cc.Sprite.Type.SLICED : cc.Sprite.Type.SIMPLE;
    sprite.trim = false;
    sprite.spriteFrame = frames.get(spec.asset);
    if (spec.type === 'button') {
      const button = node.addComponent(cc.Button);
      button.target = node;
      if (spec.pressed || spec.disabled) {
        button.transition = cc.Button.Transition.SPRITE;
        button.normalSprite = button.hoverSprite = sprite.spriteFrame;
        button.pressedSprite = frames.get(spec.pressed) ?? sprite.spriteFrame;
        button.disabledSprite = frames.get(spec.disabled) ?? sprite.spriteFrame;
      }
    }
  } else if (spec.type === 'text') {
    const label = node.addComponent(cc.Label);
    label.string = spec.text;
    label.fontSize = label.lineHeight = spec.fontSize;
    label.enableWrapText = false;
    label.overflow = cc.Label.Overflow.CLAMP;
    label.horizontalAlign = {
      left: cc.Label.HorizontalAlign.LEFT, center: cc.Label.HorizontalAlign.CENTER, right: cc.Label.HorizontalAlign.RIGHT,
    }[spec.align];
    label.verticalAlign = cc.Label.VerticalAlign.CENTER;
    label.color = cc.Color.fromHEX(new cc.Color(), spec.color);
  } else if (spec.type !== 'group') {
    throw new Error(`Unsupported UI node type: ${spec.type}`);
  }

  align(cc, node, rect, spec.anchor, parentSize);
  return node;
}

exports.load = () => {};
exports.unload = () => {};
exports.methods = {
  async build(bundle, ids) {
    const cc = require('cc');
    const parent = cc.director.getScene();
    if (!parent) throw new Error('Open a scene for import');
    const frames = new Map();
    for (const [id, uuid] of Object.entries(ids)) {
      const frame = await new Promise((resolve, reject) => cc.assetManager.loadAny({ uuid }, (error, asset) => {
        if (error) reject(error);
        else if (!(asset instanceof cc.SpriteFrame)) reject(new Error(`Missing SpriteFrame for ${id}`));
        else resolve(asset);
      }));
      frames.set(id, frame);
    }

    const root = new cc.Node(bundle.id);
    try {
      root.layer = cc.Layers.Enum.UI_2D;
      parent.addChild(root);
      root.addComponent(cc.UITransform).setContentSize(bundle.width, bundle.height);
      const sliced = new Set(bundle.assets.filter((asset) => asset.border).map((asset) => asset.id));
      const parents = new Map([['$root', { node: root, size: bundle }]]);
      for (const spec of bundle.nodes) {
        const parent = parents.get(spec.parent);
        const node = createNode(cc, spec, parent.node, parent.size, frames, sliced);
        parents.set(spec.id, { node, size: spec.rect });
      }
      drafts.set(root.uuid, root);
      return root.uuid;
    } catch (error) {
      root.removeFromParent();
      root.destroy();
      throw error;
    }
  },
  remove(uuid) {
    const { isValid } = require('cc');
    const node = drafts.get(uuid);
    drafts.delete(uuid);
    if (isValid(node)) {
      node.removeFromParent();
      node.destroy();
    }
  },
};
