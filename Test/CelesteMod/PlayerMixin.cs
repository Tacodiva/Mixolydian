using Celeste;
using Microsoft.Xna.Framework;
using Mixolydian.Common;
using Monocle;

namespace CelesteMod {

    [TypeMixin(typeof(Player))]
    public class PlayerMixin {

        [MixinFieldAccessor("Hair")]
        public PlayerHair Hair;

        [MixinFieldAccessor("StateMachine")]
        public StateMachine StateMachine;

        [MixinFieldAccessor("Sprite")]
        public PlayerSprite Sprite;

        [MixinFieldAccessor("Dashes")]
        public int Dashes;

        // [MixinFieldAccessor("MaxDashes")]
        // public int MaxDashes;
        [MixinMethodAccessor("get_MaxDashes")]
        public extern int GetMaxDashes();

        [MixinFieldAccessor("Facing")]
        public Facings Facing;

        [MixinFieldAccessor("lastDashes")]
        private int lastDashes;

        [MixinFieldAccessor("hairFlashTimer")]
        private float hairFlashTimer;

        [MethodTailMixin("UpdateHair")]
        public MixinReturn UpdateHair(bool applyGravity) {
            if (StateMachine.State == 19) {
                Hair.Color = Sprite.Color;
                applyGravity = false;
            } else if (Dashes == 0 && Dashes < GetMaxDashes()) {
                if (Sprite.Mode == PlayerSpriteMode.MadelineAsBadeline) {
                    Hair.Color = Player.UsedBadelineHairColor;
                } else {
                    Hair.Color = Player.UsedHairColor;
                }
            } else {
                Color color;
                if (lastDashes != Dashes) {
                    color = Player.FlashHairColor;
                    hairFlashTimer = 0.12f;
                } else if (!(hairFlashTimer > 0f)) {
                    color = ((Sprite.Mode == PlayerSpriteMode.MadelineAsBadeline) ? ((Dashes != 2) ? Player.NormalBadelineHairColor : Player.TwoDashesBadelineHairColor) : ((Dashes != 2) ? Player.NormalHairColor : Player.TwoDashesHairColor));
                } else {
                    color = Player.FlashHairColor;
                    hairFlashTimer -= Engine.DeltaTime;
                }
                Hair.Color = color;
            }
            Hair.Facing = Facing;
            Hair.SimulateMotion = applyGravity;
            lastDashes = Dashes;
            return MixinReturn.Return();
        }
    }
}