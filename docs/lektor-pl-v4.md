# Lektor PL — wersja 4.2.2

## Widoczne pliki i kolejne eksporty w 4.2.2

Pod **WAV lektora PL** znajduje się edytowalne pole **Miks PL**. Można wkleić
ścieżkę gotowego audio (także skopiowaną z Windows w cudzysłowie) lub użyć
**Wybierz…**. Pole **Wynikowy MKV** pokazuje pełną ścieżkę pliku wynikowego,
łącznie z katalogiem i nazwą. Można ją wkleić lub wybrać przed uruchomieniem.

**MKV z istniejącego miksu** korzysta z wideo, miksu, opcjonalnego SRT i wyniku
wskazanych w oknie. Po kliknięciu nie otwiera okien wyboru plików. Nie wymaga
WAV i zachowuje oryginalny kodek gotowego miksu. Brakujące pliki lub katalog
oraz próba zapisania MKV na pliku wejściowym zatrzymują operację. Istniejący
wynik można zastąpić po potwierdzeniu z widoczną pełną ścieżką.

**Miks + MKV** korzysta ze wskazanego wyniku; jeżeli pole jest puste, proponuje
wybór miejsca zapisu jak wcześniej. Nowo zapisany miks pojawia się w polu
**Miks PL**, także po użyciu **Tylko miks**.

Po udanym eksporcie i zamknięciu komunikatu potwierdzającego okno pozostaje
otwarte, pola i przyciski są ponownie aktywne. Można sprawdzić wskazany komplet
plików, zmienić je i wykonać kolejny eksport. Ustawienia plików pozostają
widoczne do zamknięcia okna. Wersja 4.2.2 nie zmienia silnika miksowania,
muksowania ani obsługi wymuszonych napisów.

## Bezpośredni dostęp z paska narzędzi w 4.2.1

Ikona łączenia na głównym pasku narzędzi, w grupie otwierania i zapisu plików,
otwiera okno **Lektor PL — miks audio i MKV**. Jest dostępna także bez
wczytanych napisów i bez włączania generatora TTS.

Jeżeli w głównym oknie jest otwarte wideo, jego ścieżka jest podpowiadana.
W oknie Lektor PL można wskazać inne wideo, WAV lektora, gotowy miks oraz
opcjonalny SRT z wymuszonymi napisami PL. Sposób miksowania i tworzenia MKV
pozostaje taki sam jak w 4.2.

## Opcjonalne wymuszone napisy PL w 4.2

W oknie eksportu pole **Wymuszone napisy PL (SRT)** pozwala wskazać dodatkowy
plik SRT przez **Wybierz…**. Pole jest początkowo puste i nie jest zapamiętywane
między otwarciami okna. **Wyczyść** wyłącza dodawanie tej ścieżki.

Wskazany SRT jest dodawany do MKV jako osobna ścieżka `Polski - napisy wymuszone`,
z językiem `pol`, Enabled=1, Default=1 i Forced=1. Zachowujemy tekst i czasy;
źródłowy SRT pozostaje niezmieniony. Napisy nie są przekazywane do generatora
TTS ani wtapiane w obraz. Wybór działa
zarówno przy **Miks + MKV**, jak i **MKV z istniejącego miksu**.
**Tylko miks** ignoruje pole SRT. Bez wybranego SRT eksport działa jak w 4.1.1.

Przed miksowaniem sprawdzamy istnienie i poprawność SRT. Do MKVToolNix
przekazujemy wykryte kodowanie pliku, również dla UTF-8 bez BOM z polskimi
znakami. Kontrola wyniku uwzględnia dodatkową ścieżkę i jej flagi.
Wszystkie oryginalne ścieżki, rozdziały i załączniki nadal pozostają zachowane;
oryginalne napisy mają Default=0 i Forced=0. Ostateczny automatyczny wybór
napisów zależy także od ustawień i obsługi flag przez odtwarzacz.

Zakres 4.2 obejmuje wyłącznie opcjonalny SRT. Synteza i synchronizacja TTS,
miksowanie, dynamiczne ściszanie oraz kodowanie audio pozostają bez zmian.

## Poprawka kontroli MKV w 4.1.1

Grafiki okładek PNG/JPEG bywają opisane przez FFmpeg jako `Video (attached pic)`.
W 4.1 kontrola mogła porównać taką grafikę z nowym audio PL i odrzucić poprawny
MKV komunikatem o zmianie kodeka. W 4.1.1 okładki są traktowane jako załączniki;
sprawdzanie pakietów i czasu obrazu dotyczy wyłącznie właściwego filmu.

Załączniki (okładki, czcionki, pozostałe pliki) są sprawdzane niezależnie:
porównujemy nazwy, typy MIME, rozmiary i SHA-256 rzeczywistej zawartości.
Do odczytu służy **mkvextract**, dołączony wraz z mkvmerge w katalogu MKVToolNix.
Kolejność i numery załączników mogą się zmienić; ich zawartość musi być identyczna.
Pełna ścieżka załącznika może zostać zastąpiona samą nazwą pliku przez MKVToolNix.
Akceptowane są równoważne starsze i nowsze nazwy MIME czcionek.
Kontrola typów i kodeków obrazu, audio oraz napisów pozostaje aktywna.

Przy rozbieżności komunikat podaje konkretny indeks, typ i kodek ścieżki.
`tools-log.txt` zawiera zestawienie ścieżek źródła, miksu i wyniku do diagnostyki.
Miksowanie, AC3, dynamiczne ściszanie i synteza TTS są niezmienione.

## Poprawka tworzenia MKV

W 4.1 kontener tworzy **mkvmerge z MKVToolNix**, ten sam silnik, którego używa
MKVToolNix GUI. FFmpeg nadal odpowiada za niezmienione miksowanie audio i AC3.
Usunięto dotychczasowy remuks FFmpeg z generowaniem PTS. Obraz nie jest kodowany
ponownie; mkvmerge odczytuje źródłowe znaczniki czasu i dane kodeka.

Paczka Windows 4.1 zawiera MKVToolNix. Program wykrywa też instalację w Program
Files lub PATH. Jeśli masz MKVToolNix w innym miejscu, wskaż **mkvmerge.exe**
w polu **MKVToolNix / mkvmerge**. **Tylko miks** nie wymaga MKVToolNix.
Brak mkvmerge nie uruchamia dawnej metody FFmpeg jako zastępstwa.

Aby wykorzystać już wygenerowane poprawne AC3, wybierz **MKV z istniejącego
miksu…**, wskaż oryginalny film i AC3. Nie potrzeba WAV ani ponownego TTS.
Wybierz nową nazwę wynikowego MKV.

Kontrola porównuje także znaczniki czasu każdej klatki obrazu i początek ścieżek.
Testy regresji dekodują wszystkie klatki ruchomego H.264 i HEVC (8 i 10 bitów) z klatkami B,
sprawdzają identyczność obrazu i przewijanie oraz wariant ze zmienną częstością
klatek. Zastępuje to poleganie wyłącznie na liczbie pakietów nieruchomego filmu.

Wersja 4 bazuje na `azure-f0-wave-merge-v3` (commit `ec36837`). Zachowuje
syntezę Azure, korektę synchronizacji i szybkie scalanie WAV z wersji 3.

## Procedura

1. Wczytaj poprawiony SRT w głównym oknie Subtitle Edit i otwórz Text to speech.
2. Włącz **Moduł Lektor PL v4**. Przycisk **Wybierz źródłowe wideo…** pozwala
   wczytać film bez zamykania TTS. Wideo otwarte wcześniej w programie jest
   używane automatycznie.
3. Włącz **Add audio to video file** i wygeneruj lektora normalnie. Po korekcie
   klipów program scala lektora i pyta o miejsce zapisu zsynchronizowanego WAV.
4. Po zapisaniu WAV otwiera się okno **Lektor PL — wersja 4.2.2**. Wybierz konkretną
   ścieżkę źródłową, kodek, bitrate, poziomy i tryb miksowania.
   Opcjonalnie wskaż **Wymuszone napisy PL (SRT)**.
5. **Miks + MKV** zapisuje niezależny miks audio, następnie tworzy i sprawdza MKV.

Przycisk **Lektor PL: istniejący WAV…** uruchamia eksport bez generowania TTS.
W samym oknie eksportu można zmienić źródłowe wideo i WAV.

## Ustawienia i pliki

Domyślne ustawienia: AC3 384 kb/s, stereo, 48 kHz, lektor 100%, oryginał 100%,
dynamiczne ściszanie. Próg -30 dB, ratio 8, czas ściszania 80 ms, czas
przywracania 500 ms. Dynamiczne ściszanie reaguje na rzeczywisty poziom WAV,
nie na napisy; nie obiecuje stałego procentu tłumienia. W trybie stałym ustaw
np. oryginał na 15–25%. Limiter ogranicza sumę do około -1 dBFS i kompensuje
własne opóźnienie. Nie dodajemy żadnego dodatkowego przesunięcia lektora.

Dostępne kodeki: AC3, AAC (plik M4A), MP3, FLAC. Bitrate dobiera się z wartości
obsługiwanych przez dany kodek; dla FLAC nie ma bitrate. Poziomy miksowania
i ustawienia kodowania są zapamiętywane oddzielnie od ustawień dawnego eksportu.

Dla wyniku `Film_PL.mkv` miks jest zapisywany np. jako `Film_PL_MIX_PL.ac3`.
WAV zapisany w poprzednim etapie pozostaje w wybranym wcześniej miejscu.

- **Tylko miks** zapisuje samo audio.
- **MKV z istniejącego miksu** pozwala wskazać gotowy miks, zachowuje jego kodek
  i nie wymaga obecności WAV ani ponownego kodowania miksu.

MKV zachowuje wszystkie rozpoznane strumienie źródłowe, metadane, rozdziały
i załączniki. Obraz i oryginalne ścieżki są kopiowane bez rekompresji. Nowy miks
jest pierwszą ścieżką audio, z językiem `pol`, tytułem `Polski lektor PL`,
Default=1, Forced=0. Pozostałe audio oraz oryginalne napisy mają Default=0 i Forced=0;
inne właściwości ścieżek pozostają zachowane. Ścieżki nadal można wybierać ręcznie.
Odtwarzacz może kierować się własnymi preferencjami językowymi.

Miks jest dopasowany do dłuższego z dwóch plików: źródła i WAV. Brakujący
koniec krótszego audio zostaje uzupełniony ciszą. Oryginalnego obrazu ani audio
nie skracamy. Lektor dłuższy niż film może więc przedłużyć czas kontenera;
jego końcowe kwestie wymagają kontroli synchronizacji w TTS.

## Kontrola i wznowienie

Audio i kontrola używają FFmpeg skonfigurowanego w Subtitle Edit. Nie wymaga ffprobe.
MKV tworzy mkvmerge z MKVToolNix; identyfikatory ścieżek odczytuje z jego JSON,
ponieważ nie są to indeksy strumieni FFmpeg. Ostrzeżenia mkvmerge zatrzymują
zastąpienie docelowego pliku i są pokazywane użytkownikowi do sprawdzenia.
Wynik powstaje w pliku tymczasowym obok docelowego i zastępuje go dopiero
po udanej kontroli. Sprawdzamy liczbę i kodeki ścieżek, rozdziały, język i flagi,
liczbę pakietów oraz długość każdego oryginalnego strumienia audio/wideo.
Kontrola odczytuje skompresowane pakiety całego filmu, więc duże pliki mogą
wymagać dodatkowego czasu. Ostateczny miks także jest sprawdzany przed zapisem.

Anulowanie lub błąd nie usuwa zapisanych WAV ani ukończonego miksu. Istniejący
MKV pozostaje nietknięty, jeśli próba zastąpienia nie przejdzie kontroli.
Plików źródłowych nie wolno wybrać jako wynikowych. Strumień nieobsługiwany
przez MKV powoduje błąd eksportu, zamiast cichego pominięcia.

V4 miksuje wybrane oryginalne audio z lektorem. Oddzielne funkcje usuwania
oryginalnej mowy i dodawania muzyki należą do dawnego eksportu; w trybie v4
nie są stosowane. Aby używać dawnej procedury, wyłącz **Moduł Lektor PL v4**.

## Sprawdzenie zmian

`LektorExportTests` sprawdzają cztery kodeki i oba tryby na syntetycznym filmie
z dwiema ścieżkami audio, dwoma napisami, rozdziałem i załącznikiem. Obejmują
wybór drugiej ścieżki, odzyskanie głośności w ciszy, niezmienność wejść,
ponowny mux bez WAV, anulowanie i błąd nieobsługiwanego strumienia.
Dodatkowe testy 4.2 obejmują SRT w obu trybach tworzenia MKV, polskie znaki
w UTF-8 bez BOM, zachowanie treści i czasów po ekstrakcji, flagi ścieżek,
dekodowanie i przewijanie H.264, niepoprawny/brakujący SRT, ochronę plików
wejściowych oraz eksport samego miksu z nieważną ścieżką SRT.
Wymagają działających FFmpeg i mkvmerge; `FFMPEG_TEST_PATH` oraz
`MKVMERGE_TEST_PATH` mogą wskazywać ich pełne ścieżki.
Workflow `Lektor PL v4 - Windows x64` wykonuje testy i tworzy paczkę portable ZIP.
