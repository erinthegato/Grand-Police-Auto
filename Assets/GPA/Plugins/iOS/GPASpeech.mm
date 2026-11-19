// Native text-to-speech for the police radio on iPhone/iPad (called from RadioSpeech.cs).
#import <AVFoundation/AVFoundation.h>

static AVSpeechSynthesizer* gpaSynth = nil;

extern "C" {

void GPASpeak(const char* text, int voice, float rate, float pitch, float volume)
{
    if (text == NULL) return;
    if (gpaSynth == nil) gpaSynth = [[AVSpeechSynthesizer alloc] init];
    if (gpaSynth.speaking) [gpaSynth stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];

    AVSpeechUtterance* u = [AVSpeechUtterance speechUtteranceWithString:[NSString stringWithUTF8String:text]];
    NSArray* voices = [[AVSpeechSynthesisVoice speechVoices] filteredArrayUsingPredicate:
                       [NSPredicate predicateWithFormat:@"language BEGINSWITH 'en-US'"]];
    if (voices.count > 0) u.voice = voices[(NSUInteger)voice % voices.count];
    else u.voice = [AVSpeechSynthesisVoice voiceWithLanguage:@"en-US"];
    u.rate = rate;
    u.pitchMultiplier = pitch;
    u.volume = volume;
    [gpaSynth speakUtterance:u];
}

void GPAStopSpeaking(void)
{
    if (gpaSynth != nil) [gpaSynth stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
}

bool GPAIsSpeaking(void)
{
    return gpaSynth != nil && gpaSynth.speaking;
}

}
