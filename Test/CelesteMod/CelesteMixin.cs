using System;
using System.IO;
using Celeste;
using Mixolydian.Common;
using Microsoft.Xna.Framework;
using Monocle;

namespace CelesteMod {

    [ClassMixin(typeof(WaveDashPage01))]
    public class CelesteMixin {

        // [MixinTargetMethod]

        [MethodMixin("Render")]
        public MixinReturn HelloWorld() {
            Vector2 position = new Vector2(1920 / 2f, 1080 / 2f + 80f);
            float x = 1f + Ease.BigBackIn(1f - 1) * 2f;
            float y = 0.25f + Ease.BigBackIn(1) * 0.75f;
            ActiveFont.Draw("Pee is stored in the balls", position, new Vector2(0.5f, 0.5f), new Vector2(x, y), Color.Black * 0.8f);
            return MixinReturn.Return();
        }
    }
}