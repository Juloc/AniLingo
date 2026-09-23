(function () {
    const root = document.querySelector('[data-kana-challenge]');
    const catalogNode = document.getElementById('kana-catalog');
    const tokenForm = document.getElementById('kana-answer-token');
    const csrf = tokenForm ? tokenForm.querySelector('input[name="__RequestVerificationToken"]') : null;
    const entries = catalogNode ? JSON.parse(catalogNode.textContent || '[]') : [];
    let challengeIndex = 0;
    let current = null;
    let voices = [];

    function refreshVoices() {
        if ('speechSynthesis' in window) {
            voices = window.speechSynthesis.getVoices();
        }
    }

    refreshVoices();
    if ('speechSynthesis' in window) {
        window.speechSynthesis.addEventListener('voiceschanged', refreshVoices);
    }

    function speak(text, rate) {
        if (!('speechSynthesis' in window)) {
            if (root) {
                const feedback = root.querySelector('[data-challenge-feedback]');
                if (feedback) feedback.textContent = 'Auf diesem Gerät ist keine Sprachausgabe verfügbar.';
            }
            return;
        }

        window.speechSynthesis.cancel();
        const utterance = new SpeechSynthesisUtterance(text);
        utterance.lang = 'ja-JP';
        utterance.rate = Number.isFinite(rate) ? Math.max(0.5, Math.min(1.2, rate)) : 0.9;
        const japaneseVoice = voices.find(function (voice) {
            return (voice.lang || '').toLowerCase().startsWith('ja');
        });
        if (japaneseVoice) utterance.voice = japaneseVoice;
        window.speechSynthesis.speak(utterance);
    }

    document.querySelectorAll('[data-speak]').forEach(function (button) {
        button.addEventListener('click', function () {
            speak(button.getAttribute('data-speak') || '', Number(button.getAttribute('data-rate') || '0.9'));
        });
    });

    document.querySelectorAll('[data-reveal-sentence]').forEach(function (button) {
        button.addEventListener('click', function () {
            const card = button.closest('.sentence-card');
            const answer = card ? card.querySelector('.sentence-answer') : null;
            if (!answer) return;
            answer.hidden = false;
            button.hidden = true;
        });
    });

    document.querySelectorAll('.sentence-token').forEach(function (button) {
        button.addEventListener('click', function () {
            const card = button.closest('.sentence-card');
            const details = card ? card.querySelector('[data-token-details]') : null;
            if (!details) return;
            const surface = button.getAttribute('data-token-surface') || '';
            const reading = button.getAttribute('data-token-reading') || '';
            const meaning = button.getAttribute('data-token-meaning') || '';
            details.textContent = surface + (reading ? ' · ' + reading : '') + (meaning ? ' · ' + meaning : '');
        });
    });

    if (!root || entries.length === 0) return;

    const modeNode = root.querySelector('[data-challenge-mode]');
    const promptNode = root.querySelector('[data-challenge-prompt]');
    const choicesNode = root.querySelector('[data-challenge-choices]');
    const feedbackNode = root.querySelector('[data-challenge-feedback]');
    const startButton = root.querySelector('[data-challenge-start]');
    const nextButton = root.querySelector('[data-challenge-next]');
    const audioButton = root.querySelector('[data-challenge-audio]');
    const modes = ['kana-to-romaji', 'romaji-to-kana', 'audio-to-kana'];

    function shuffle(values) {
        const copy = values.slice();
        for (let i = copy.length - 1; i > 0; i--) {
            const j = Math.floor(Math.random() * (i + 1));
            const tmp = copy[i];
            copy[i] = copy[j];
            copy[j] = tmp;
        }
        return copy;
    }

    function makeChoices(target, mode) {
        const field = mode === 'kana-to-romaji' ? 'romaji' : 'symbol';
        const expected = target[field];
        const pool = shuffle(entries.map(function (entry) { return entry[field]; })
            .filter(function (value, index, values) { return values.indexOf(value) === index && value !== expected; }));
        return shuffle([expected].concat(pool.slice(0, 3)));
    }

    function modeLabel(mode) {
        if (mode === 'kana-to-romaji') return 'Zeichen → Lesen';
        if (mode === 'romaji-to-kana') return 'Romaji → Zeichen';
        return 'Hören → Zeichen';
    }

    function renderChallenge() {
        current = entries[Math.floor(Math.random() * entries.length)];
        const mode = modes[challengeIndex % modes.length];
        challengeIndex += 1;
        current.mode = mode;

        modeNode.textContent = modeLabel(mode);
        feedbackNode.textContent = '';
        choicesNode.innerHTML = '';
        nextButton.hidden = true;
        startButton.hidden = true;
        audioButton.hidden = mode !== 'audio-to-kana';

        if (mode === 'kana-to-romaji') {
            promptNode.textContent = current.symbol;
        } else if (mode === 'romaji-to-kana') {
            promptNode.textContent = current.romaji;
        } else {
            promptNode.textContent = 'どれ？';
            speak(current.symbol, 0.82);
        }

        makeChoices(current, mode).forEach(function (choice) {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'challenge-choice';
            button.textContent = choice;
            button.addEventListener('click', function () { answerChallenge(button, choice); });
            choicesNode.appendChild(button);
        });
    }

    async function answerChallenge(button, answer) {
        if (!current) return;
        choicesNode.querySelectorAll('button').forEach(function (item) { item.disabled = true; });

        const data = new URLSearchParams();
        data.set('termId', current.termId);
        data.set('mode', current.mode);
        data.set('answer', answer);
        if (csrf) data.set('__RequestVerificationToken', csrf.value);

        try {
            const response = await fetch(window.location.pathname + '?handler=Answer', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded;charset=UTF-8' },
                body: data.toString()
            });
            if (!response.ok) throw new Error('HTTP ' + response.status);
            const result = await response.json();

            button.classList.add(result.correct ? 'correct' : 'wrong');
            feedbackNode.textContent = result.correct
                ? 'Richtig.'
                : 'Noch einmal: ' + result.expected;
            if (!result.correct) {
                choicesNode.querySelectorAll('button').forEach(function (item) {
                    if (item.textContent === result.expected) item.classList.add('correct');
                });
            }
        } catch (error) {
            feedbackNode.textContent = 'Antwort konnte nicht gespeichert werden.';
        }

        nextButton.hidden = false;
        nextButton.focus();
    }

    audioButton.addEventListener('click', function () {
        if (current) speak(current.symbol, 0.82);
    });
    startButton.addEventListener('click', renderChallenge);
    nextButton.addEventListener('click', renderChallenge);
})();
