using Celeste;
using Microsoft.Xna.Framework;
using Mixolydian.Common;
using Monocle;

namespace CelesteMod {

    [TypeMixin(typeof(Player))]
    public class PlayerMixin {

        [MixinThis]
        public readonly Player @this;

        [MixinFieldAccessor("lastDashes")]
        private int lastDashes;

        [MixinFieldAccessor("hairFlashTimer")]
        private float hairFlashTimer;

        [MethodTailMixin("UpdateHair")]
        public MixinReturn UpdateHair(bool applyGravity) {
            if (@this.StateMachine.State == 19) {
                @this.Hair.Color = @this.Sprite.Color;
                applyGravity = false;
            } else if (@this.Dashes == 0 && @this.Dashes < @this.MaxDashes) {
                if (@this.Sprite.Mode == PlayerSpriteMode.MadelineAsBadeline) {
                    @this.Hair.Color = Player.UsedBadelineHairColor;
                } else {
                    @this.Hair.Color = Player.UsedHairColor;
                }
            } else {
                Color color;
                if (lastDashes != @this.Dashes) {
                    color = Player.FlashHairColor;
                    hairFlashTimer = 0.12f;
                } else if (!(hairFlashTimer > 0f)) {
                    color = ((@this.Sprite.Mode == PlayerSpriteMode.MadelineAsBadeline) ? ((Dashes != 2) ? Player.NormalBadelineHairColor : Player.TwoDashesBadelineHairColor) : ((@this.Dashes != 2) ? Player.NormalHairColor : Player.TwoDashesHairColor));
                } else {
                    color = Player.FlashHairColor;
                    hairFlashTimer -= Engine.DeltaTime;
                }
                @this.Hair.Color = color;
            }
            @this.Hair.Facing = @this.Facing;
            @this.Hair.SimulateMotion = applyGravity;
            lastDashes = @this.Dashes;
            return MixinReturn.Return();
        }
    }
}