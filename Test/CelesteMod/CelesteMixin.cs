using System;
using System.Collections.Generic;
using System.IO;
using Celeste;
using Microsoft.Xna.Framework;
using Mixolydian.Common;
using Monocle;

namespace CelesteMod {

    [TypeMixin(typeof(WaveDashPage01))]
    public class CelesteMixin {

        // [MixinTargetMethod]

        [MethodTailMixin("Render")]
        public MixinReturn HelloWorld() {
            Vector2 position = new Vector2(1920 / 2f, 1080 / 2f + 80f);
            float x = 1f + Ease.BigBackIn(1f - 1) * 2f;
            float y = 0.25f + Ease.BigBackIn(1) * 0.75f;
            ActiveFont.Draw("Piss is stored in the balls ;)", position, new Vector2(0.5f, 0.5f), new Vector2(x, y), Color.Black * 0.8f);
            return MixinReturn.Return();
        }
    }

    [TypeMixin(typeof(PlayerHair))]
    public class HairMixin {

        [MixinThis]
        private readonly PlayerHair @this;

        [MixinFieldAccessor("Sprite")]
        public PlayerSprite Sprite;

        [MixinFieldAccessor("Nodes")]
        public List<Vector2> Nodes;

        [MixinFieldAccessor("DrawPlayerSpriteOutline")]
        public bool DrawPlayerSpriteOutline;

        [MixinFieldAccessor("Alpha")]
        public float Alpha;

        [MixinFieldAccessor("bangs")]
        private List<MTexture> bangs;

        [MixinMethodAccessor("GetHairScale")]
        private extern Vector2 GetHairScale(int index);

        [MixinFieldAccessor("Color")]
        public Color Color;

        [MixinFieldAccessor("Border")]
        public Color Border;

        public const int HairLength = 25;

        [MethodTailMixin("AfterUpdate")]
        public MixinReturn HelloWorld() {
            Sprite.HairCount = HairLength;
            return MixinReturn.Continue();
        }

        public float time;

        [MethodTailMixin("Render")]
        public MixinReturn Render() {
            time += Engine.DeltaTime;
            if (!Sprite.HasHair) {
                return MixinReturn.Return();
            }
            Vector2 origin = new Vector2(5f, 5f);
            Color color = Border * Alpha;
            Color color2 = Color * Alpha;

            if (DrawPlayerSpriteOutline) {
                Color color3 = Sprite.Color;
                Vector2 position = Sprite.Position;
                Sprite.Color = color;
                Sprite.Position = position + new Vector2(0f, -1f);
                Sprite.Render();
                Sprite.Position = position + new Vector2(0f, 1f);
                Sprite.Render();
                Sprite.Position = position + new Vector2(-1f, 0f);
                Sprite.Render();
                Sprite.Position = position + new Vector2(1f, 0f);
                Sprite.Render();
                Sprite.Color = color3;
                Sprite.Position = position;
            }
            Nodes[0] = Calc.Floor(Nodes[0]);
            if (color.A > 0) {
                for (int i = 0; i < Sprite.HairCount; i++) {
                    // color.G = (byte) (Math.Sin(time + (i / 2f)) * 127f + 128f);
                    int hairFrame = Sprite.HairFrame;
                    MTexture obj = ((i == 0) ? bangs[hairFrame] : GFX.Game["characters/player/hair00"]);
                    Vector2 hairScale = GetHairScale(i);
                    obj.Draw(Nodes[i] + new Vector2(-1f, 0f), origin, color, hairScale);
                    obj.Draw(Nodes[i] + new Vector2(1f, 0f), origin, color, hairScale);
                    obj.Draw(Nodes[i] + new Vector2(0f, -1f), origin, color, hairScale);
                    obj.Draw(Nodes[i] + new Vector2(0f, 1f), origin, color, hairScale);
                }
            }

            Color[] trans = new Color[] {
                new Color(0x55, 0xCD, 0xFC),
                new Color(0xF7, 0xA8, 0xB8),
                new Color(0xFF, 0xFF, 0xFF),
                new Color(0xF7, 0xA8, 0xB8),
                new Color(0x55, 0xCD, 0xFC),
            };

            Color[] pan = new Color[] {
                new Color(0xFF, 0x1B, 0x8D),
                new Color(0xFF, 0xDA, 0x00),
                new Color(0x1B, 0xB3, 0xFF),
            };

            Color[] gay = new Color[] {
                new Color(0xE5, 0x00, 0x00),
                new Color(0xFF, 0x8D, 0x00),
                new Color(0xFF, 0xEE, 0x00),
                new Color(0x02, 0x81, 0x21),
                new Color(0x00, 0x4C, 0xFF),
                new Color(0x77, 0x00, 0x88),
            };

            Color[] french = new Color[] {
                new Color(0x00, 0x26, 0x54),
                new Color(0xFF, 0xFF, 0xFF),
                new Color(0xED, 0x29, 0x39),
            };

            Color[] feather = new Color[] {
                new Color(0xFF, 0x1B, 0x8D),
            };

            for (int num = Sprite.HairCount - 1; num >= 0; num--) {
                Color[] flag = null;
                if (color2 == Player.NormalHairColor) {
                    flag = gay;
                } else if (color2 == Player.UsedHairColor) {
                    flag = trans;
                } else if (color2 == Player.TwoDashesHairColor) {
                    flag = pan;
                } else if (color2 == Player.FlashHairColor) { 
                    flag = null;
                } else if (color2 == Player.FlyPowerHairColor) {
                    flag = pan;
                } else {
                    flag = french;
                }
                Color hairColor = color2;
                if (flag != null)
                    hairColor = flag[(int)Math.Floor((num / (float)Sprite.HairCount) * flag.Length)];

                int hairFrame2 = Sprite.HairFrame;
                ((num == 0) ? bangs[hairFrame2] : GFX.Game["characters/player/hair00"]).Draw(Nodes[num], origin, hairColor, GetHairScale(num));
            }
            return MixinReturn.Return();
        }

        [MethodTailMixin("Start")]
        public MixinReturn Start() {
            Sprite.HairCount = HairLength;

            while (Nodes.Count < HairLength) {
                Nodes.Add(Vector2.Zero);
            }
            return MixinReturn.Continue();
        }
    }
}
