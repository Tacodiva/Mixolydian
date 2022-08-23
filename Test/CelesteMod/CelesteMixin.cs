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

        [MethodMixin("Render")]
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

        [MethodMixin("AfterUpdate")]
        public MixinReturn HelloWorld() {
            Sprite.HairCount = 50;
            return MixinReturn.Continue();
        }

        public float time;

        [MethodMixin("Render")]
        public MixinReturn Render() {
            time += Engine.DeltaTime;
            if (!Sprite.HasHair)
            {
                return MixinReturn.Return();
            }
            Vector2 origin = new Vector2(5f, 5f);
            Color color = Border * Alpha;
            Color color2 = Color * Alpha;

            if (DrawPlayerSpriteOutline)
            {
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
            if (color.A > 0)
            {
                for (int i = 0; i < Sprite.HairCount; i++)
                {
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
            for (int num = Sprite.HairCount - 1; num >= 0; num--)
            {
                float rMul = color2.R;
                float bMul = color2.B;
                color2.R = (byte) ((Math.Sin(time * 3 + (num / 1.5f)) * 127f + 128f) * rMul);
                color2.G = (byte) (Math.Sin(time * 4 + (num / 2f)) * 127f + 128f);
                color2.B = (byte) ((Math.Sin(time * 5 + (num / 2.5f)) * 127f + 128f) * bMul);
                int hairFrame2 = Sprite.HairFrame;
                ((num == 0) ? bangs[hairFrame2] : GFX.Game["characters/player/hair00"]).Draw(Nodes[num], origin, color2, GetHairScale(num));
            }
            return MixinReturn.Return();
        }

        [MethodMixin("Start")]
        public MixinReturn Start() {
            Sprite.HairCount = 50;

            while (Nodes.Count < 50) {
                Nodes.Add(Vector2.Zero);
            }
            return MixinReturn.Continue();
        }
    }
}
