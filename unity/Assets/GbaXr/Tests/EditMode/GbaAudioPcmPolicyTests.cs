using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaAudioPcmPolicyTests
    {
        [Test]
        public void Signed16ConversionMapsTheNativeRangeToUnityFloat()
        {
            Assert.That(GbaAudioPcmPolicy.Int16ToFloat(short.MinValue), Is.EqualTo(-1f));
            Assert.That(GbaAudioPcmPolicy.Int16ToFloat((short)0), Is.Zero);
            Assert.That(GbaAudioPcmPolicy.Int16ToFloat(short.MaxValue), Is.EqualTo(32767f / 32768f));
        }

        [Test]
        public void StereoIsPreservedAndMonoIsDownmixed()
        {
            var stereo = new float[2];
            GbaAudioPcmPolicy.WriteStereoFrame(stereo, 0, 2, 0.25f, -0.5f);
            Assert.That(stereo, Is.EqualTo(new[] { 0.25f, -0.5f }));

            var mono = new float[1];
            GbaAudioPcmPolicy.WriteStereoFrame(mono, 0, 1, 0.25f, -0.5f);
            Assert.That(mono[0], Is.EqualTo(-0.125f));
        }

        [Test]
        public void ExtraOutputChannelsRemainSilent()
        {
            var surround = new[] { 9f, 9f, 9f, 9f };
            GbaAudioPcmPolicy.WriteStereoFrame(surround, 0, 4, 0.25f, -0.5f);

            Assert.That(surround, Is.EqualTo(new[] { 0.25f, -0.5f, 0f, 0f }));
        }

        [Test]
        public void StereoFrameClearsAnyNonStereoChannels()
        {
            var output = new[] { 1f, 1f, 1f, 1f };
            GbaAudioPcmPolicy.WriteStereoFrame(output, 0, 4, -0.25f, 0.5f);

            Assert.That(output, Is.EqualTo(new[] { -0.25f, 0.5f, 0f, 0f }));
        }

        [Test]
        public void Native48kTo24k_ConsumesTwoNativeFramesPerOutputFrame()
        {
            var ratio = GbaAudioResamplePolicy.NativeFramesPerOutputFrame(48000, 24000);

            Assert.That(ratio, Is.EqualTo(2d));
        }

        [Test]
        public void ResampleInterpolation_PreservesStereoChannels()
        {
            Assert.That(GbaAudioResamplePolicy.Lerp(-1f, 1f, 0.25d), Is.EqualTo(-0.5f));
            Assert.That(GbaAudioResamplePolicy.Lerp(0.5f, -0.5f, 0.75d), Is.EqualTo(-0.25f));
        }
    }
}
