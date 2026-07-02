using UIMarkerEditor;

namespace UIMarkerEditor.Tests;

public sealed class UnknownMapIdPolicyChangeConfirmationTests
{
    [Fact]
    public void Evaluate_WhenSwitchingFromRejectToAllowWithWarningEnabled_RequiresConfirmation()
    {
        UnknownMapIdPolicyChangeConfirmation confirmation =
            UnknownMapIdPolicyChangeConfirmation.Evaluate(
                UnknownMapIdPolicy.RejectUnknown,
                UnknownMapIdPolicy.AllowUnknown,
                showAllowUnknownWarning: true);

        Assert.True(confirmation.RequiresConfirmation);
    }

    [Theory]
    [InlineData(UnknownMapIdPolicy.AllowUnknown, UnknownMapIdPolicy.RejectUnknown, true)]
    [InlineData(UnknownMapIdPolicy.RejectUnknown, UnknownMapIdPolicy.AllowUnknown, false)]
    [InlineData(UnknownMapIdPolicy.RejectUnknown, UnknownMapIdPolicy.RejectUnknown, true)]
    public void Evaluate_WhenSwitchDoesNotEnterPromptedAllowMode_DoesNotRequireConfirmation(
        UnknownMapIdPolicy currentPolicy,
        UnknownMapIdPolicy nextPolicy,
        bool showAllowUnknownWarning)
    {
        UnknownMapIdPolicyChangeConfirmation confirmation =
            UnknownMapIdPolicyChangeConfirmation.Evaluate(
                currentPolicy,
                nextPolicy,
                showAllowUnknownWarning);

        Assert.False(confirmation.RequiresConfirmation);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void ShouldDisableFutureConfirmation_OnlyDisablesAfterConfirmedSuppression(
        bool confirmed,
        bool doNotShowAgainChecked,
        bool expected)
    {
        Assert.Equal(
            expected,
            UnknownMapIdPolicyChangeConfirmation.ShouldDisableFutureConfirmation(
                confirmed,
                doNotShowAgainChecked));
    }
}
