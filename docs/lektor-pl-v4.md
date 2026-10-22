# Lektor PL — wersja 5.2

## Poprawki 5.2 — masowe muksowanie MKV

Naprawiono awarię widocznej tabeli przy wczytywaniu kolejnych katalogów,
przestawianiu komórek i przygotowaniu następnego zestawu. Obsługa przebudowy
wierszy korzysta teraz z tego samego zabezpieczenia co działający masowy MIX PL.
Wklejenie lub wybranie katalogu wynikowego od razu aktualizuje całą kolumnę
wyników. Start ponownie odczytuje katalog widoczny w oknie.

1. Wczytaj osobne katalogi wideo i **gotowych miksów PL** (AC3, FLAC itd.).
   Opcjonalnie wczytaj wymuszone napisy SRT PL. Każda kolumna sortuje pliki
   alfabetycznie; pliki są wiązane według wierszy. Sprawdź te powiązania.
2. Przeciąganie zamienia komórki w tej samej kolumnie. Prawy przycisk pozwala
   wybrać/wkleić plik, wyczyścić komórkę, wstawić pustą z przesunięciem w dół
   lub usunąć komórkę z przesunięciem w górę. Można usunąć cały wiersz.
3. Wskaż jeden istniejący katalog wynikowy. **Sprawdź całą tabelę** sprawdza
   kompletność, duplikaty, kolizje nazw i rzeczywiste ścieżki wszystkich plików.
   Numer sprawdzanego wiersza i jego stan są widoczne. Start powtarza kontrolę
   całej kolejki, zanim powstanie pierwszy wynik.
4. **Uruchom muksowanie kolejki** tworzy `nazwa_wideo_PL.mkv` bez ponownego
   wybierania plików. Używa dotychczasowego mechanizmu MKVToolNix i jego kontroli
   ścieżek, flag oraz znaczników czasu obrazu. Gotowy MIX PL ma język PL i jest
   domyślny; dodatkowe SRT PL mają flagi domyślne i wymuszone.
5. Błąd lub **Stop** zatrzymuje dalsze wiersze. Ukończone MKV pozostają,
   pliki tymczasowe przerwanej operacji są usuwane. Istniejący wynik, również
   utworzony przez inny program już w trakcie kolejki, nie jest nadpisywany.
6. Okno pozostaje otwarte. Można wyczyścić tabelę, wczytać kolejny zestaw,
   wskazać kolejny katalog i od razu uruchomić następną kolejkę.

Algorytm miksowania audio, łączenie części lektora WAV/AC3, masowy MIX PL,
licznik TTS i właściwy mechanizm muksowania MKV pozostają bez zmian.

## Nowości 5.1

### 5.1a — połącz części lektora po przerwaniu TTS

Na górnej belce wybierz **Lektor PL: połącz dwie części lektora** (ikona
połączenia). Ta sama funkcja jest dostępna jako **Połącz części lektora…**
w oknie masowego generowania WAV.

1. Wklej ścieżki lub wybierz dwie części czystego lektora WAV/AC3 tego samego
   odcinka. Oba pliki muszą zachowywać pierwotne pozycje wypowiedzi i ciszę
   od 00:00, tak jak pliki odzyskane po przerwaniu TTS.
2. Wskaż katalog. Nazwa jest proponowana z fragmentu przed pierwszym `_`,
   np. `S02E25.wav`; można ją poprawić przed startem.
3. **Sprawdź części** odczytuje ich długości i sprawdza nakładanie audio.
   **Połącz w jeden WAV** powtarza kontrolę, łączy części i sprawdza wynik.
4. Wynik jest jednym WAV PCM 16-bit, stereo, 48 kHz, o długości dłuższej
   części. Zachowuje ciszę, synchronizację i poziom pojedynczej wypowiedzi;
   mono jest kopiowane do obu kanałów stereo. Operacja nie uruchamia TTS
   i nie zmienia licznika znaków.
5. Jednoczesne audio w obu częściach, wskazanie tego samego wejścia lub
   istniejącego wyniku blokuje zapis. Pliki źródłowe pozostają zachowane.
   **Stop** przerywa operację i usuwa jej plik tymczasowy. Okno pozostaje otwarte.

### 5.3 — masowe tworzenie MIX PL

Na górnej belce wybierz **Lektor PL: masowe miksowanie audio MIX PL** (ikona
nuty). Otwiera się osobne, zmaksymalizowane okno.

1. Wczytaj katalog wideo i katalog zsynchronizowanych WAV lektora. Każda
   kolumna jest porządkowana alfabetycznie. Pary tworzy kolejność wierszy.
2. Sprawdź pary. Przeciągnięcie komórki zamienia ją z inną w tej samej kolumnie.
   Prawy przycisk pozwala wybrać/wkleić plik, wyczyścić, wstawić pustą komórkę
   z przesunięciem w dół lub usunąć komórkę z przesunięciem w górę. Można
   również usunąć cały zaznaczony wiersz.
3. Wybierz **Odczytaj ścieżki audio filmów**. Jeśli film ma kilka ścieżek,
   wybierz właściwą w jego wierszu, kierując się językiem, nazwą i parametrami.
   Jedyna ścieżka jest wybierana automatycznie. Wybór należy do filmu;
   przestawienie WAV nie zmienia wybranego oryginalnego audio.
4. Wskaż jeden katalog dla wszystkich wyników i wspólne ustawienia miksowania.
   Są zgodne z pojedynczym **Tylko miks**: dynamiczne/stałe ściszanie, poziomy,
   próg, ratio, attack/release, kodek i bitrate. Dostępne są AC3, AAC, MP3 i FLAC.
5. **Sprawdź całą tabelę** sprawdza komplet par, ścieżki audio, wejścia WAV,
   katalog i kolizje nazw. Ta kontrola jest zawsze wykonywana ponownie przed
   **Twórz kolejkę MIX PL**. Wyniki mają nazwy `nazwa_wideo_MiksPL.ac3`
   lub rozszerzenie odpowiednie do wybranego kodeka.
6. Kolejka używa działającego pojedynczego miksera. Zapisuje wyłącznie audio,
   gotowe do obecnego masowego tworzenia MKV. Nie wymaga SRT ani MKVToolNix.
   Błąd lub Stop zatrzymuje dalsze wiersze; ukończone miksy pozostają dostępne,
   a pliki tymczasowe przerwanej operacji są usuwane. Okno pozostaje otwarte.


## Nowości 5.0

Na górnej belce są dwa dodatkowe przyciski: **masowe SRT → WAV** (ikona fali)
i **masowe muksowanie MKV** (ikona filmu). Działają bez wczytywania napisów do
edytora. Dotychczasowa ikona montażu audio-video nadal otwiera pojedynczy Lektor PL.
Oba nowe okna oraz pojedynczy moduł Lektor PL otwierają się zmaksymalizowane.
Po zakończeniu pozostają otwarte.

### 5.1 — kolejka SRT → WAV

1. Dodaj przygotowane pliki SRT w UTF-8. Strzałki zmieniają kolejność zaznaczonego
   wiersza; checkbox w pierwszej kolumnie włącza lub wyłącza plik z kolejki.
2. Wybierz wspólny silnik, głos, region/model i ustawienia dźwięku. Masowy tryb
   czyta wszystkie SRT jednym głosem. Nie usuwa ani nie łączy linii i nie zmienia
   czasów SRT. Klonowanie głosu z osobnych filmów pozostaje w trybie pojedynczym.
3. Wskaż jeden istniejący katalog WAV. Wynik to `nazwa-SRT.wav` dla każdego pliku.
   Istniejące wyniki i powtarzające się nazwy blokują start; nie są nadpisywane.
4. Tabela pokazuje znaki każdego SRT oraz podsumowanie całej kolejki. **Sprawdź
   kolejkę** dodatkowo sprawdza pliki, zapisane fragmenty i saldo. Gotowe fragmenty
   są używane automatycznie, a wymagane znaki pomniejszane o pracę już wykonaną.
   Ostateczna kontrola jest zawsze powtarzana przed startem. Za małe saldo dla
   całej kolejki blokuje wysyłanie; odznacz część plików i sprawdź ponownie.
5. Każdy SRT przechodzi dotychczasowe generowanie, korektę szybkości,
   przetwarzanie dźwięku i zsynchronizowane łączenie z ciszą w przerwach.
   Nie ma osobnych pytań o katalog lub nazwę każdego pełnego WAV.
6. Wyczerpanie limitu zatrzymuje kolejkę przed kolejnymi plikami. Program pyta,
   czy dla bieżącego SRT zapisać brakujące linie i częściowy WAV. Po zgodzie zapisuje
   je w tym samym katalogu jako `nazwa_pozostale.srt` i `nazwa_czesciowy.wav`
   (z bezpiecznym numerem, jeśli nazwa już istnieje). Zachowuje oryginalne czasy;
   nie pobiera ponownie TTS. Surowe fragmenty i manifest postępu pozostają trwałe.
7. Ukończone pliki są automatycznie odznaczane. Po korekcie salda można ponownie
   uruchomić pozostałe pozycje; poprawne fragmenty bieżącego pliku zostaną użyte.
   Stop lub błąd zatrzymuje kolejkę, a ukończone WAV pozostają w katalogu.

### 5.2 — tabela masowego muksowania

1. Wskaż osobne katalogi wideo, **gotowych miksów PL** (np. AC3) i opcjonalnych
   wymuszonych SRT. Wczytywane są pliki bezpośrednio z katalogu, bez podkatalogów.
   Każda kolumna jest sortowana alfabetycznie, a powiązania wynikają wyłącznie
   z numeru wiersza. Program nie zgaduje na podstawie podobieństwa nazw.
2. Każdy wiersz pokazuje wideo, Miks PL, opcjonalny SRT, wynikowy MKV i stan.
   Przeciągnięcie komórki na inną w tej samej kolumnie zamienia je miejscami.
   Menu prawego przycisku pozwala wybrać plik, wkleić ścieżkę, wyczyścić komórkę,
   wstawić pustą z przesunięciem w dół lub usunąć komórkę z podniesieniem listy.
   Pozostałe kolumny pozostają na swoich miejscach. Krótsze listy mają puste komórki.
3. Wskaż jeden wspólny katalog wynikowy. Nazwa wyniku to `nazwa-wideo_PL.mkv`.
   Pusta komórka SRT oznacza brak dodawania wymuszonych napisów dla tego filmu.
   Wideo i Miks PL są wymagane w każdym wierszu.
4. Przed pierwszym wynikiem sprawdzana jest cała tabela: istnienie plików,
   poprawność SRT, obraz w wideo, dokładnie jedna ścieżka w gotowym miksie,
   powtórzone przypisania, kolizje nazw i możliwość zapisu. Istniejący MKV blokuje
   start; nie jest nadpisywany. **Sprawdź całą tabelę** umożliwia kontrolę bez startu.
5. Muksowanie jest sekwencyjne i używa niezmienionego mechanizmu MKVToolNix.
   Oryginalne ścieżki i obraz zostają zachowane bez ponownego kodowania.
   Miks PL jest domyślnym polskim audio. Dodany SRT ma język PL i flagi domyślny
   oraz wymuszony. Każdy wynik przechodzi dotychczasową kontrolę ścieżek i czasu.
6. Stop lub błąd zatrzymuje kolejne wiersze; ukończone MKV pozostają w katalogu.
   Zakończone wiersze można usunąć przyciskiem „Usuń zaznaczony wiersz”;
   „Wyczyść tabelę” przygotowuje okno do następnego zestawu. Okno zostaje otwarte.

### Stałe statystyki

Pod wyborem silnika pola „Do wysłania”, „Dostępne” i „Po generowaniu” mają
stałą lokalizację i rozmiar. Aktualizowane są wartości istniejących kontrolek,
bez przebudowywania panelu. Komunikat również ma stałą wysokość; szczegóły
rezerwacji i kont są dostępne w podpowiedzi. Zasady rozliczania z 4.2.3 pozostają.

## Kontrola znaków TTS w 4.2.3

1. W oknie tworzenia lektora, bezpośrednio na prawo od wyboru silnika, znajduje się
   przycisk ustawień **Limit i licznik znaków TTS**. Dotychczasowe ustawienia silnika
   pozostają osobnym przyciskiem.
2. Włącz kontrolę lokalnego limitu. Ustaw dzień resetu (domyślnie 1), miesięczną
   liczbę znaków i opcjonalnie liczbę dostępną do najbliższego resetu. Wartość `0`
   jest prawidłowym saldem; puste pole zachowuje saldo. Przy pierwszym włączeniu
   bez tego pola przyjmowany jest pełny wpisany limit miesięczny.
3. Pod wyborem silnika widać znaki pozostałe do wysłania, dostępne saldo i wynik
   przewidywany po generowaniu. Znaczniki czasu, numery bloków i usunięte znaczniki
   formatowania nie są wysyłane ani liczone. Pomijane przez użytkownika linie
   dźwiękowe również nie zużywają limitu. Przed generowaniem następuje ostateczna
   kontrola całego tekstu, także przy przypisaniu różnych silników do postaci.
4. Za małe saldo blokuje start bez wysłania tekstu do TTS. Test głosu w tym oknie
   również korzysta z licznika. Kontrola jest opcjonalna, np. dla silników lokalnych.

### Trwałość i rozliczanie

- Ustawienia, salda i rezerwacje są zapisywane atomowo w `tts-usage.json` w katalogu
  danych programu (w wydaniu portable obok programu). Klucze API nie są kopiowane
  do tego pliku: profile rozróżnia skrót danych konta i usługi; Azure uwzględnia
  klucz oraz region. Głosy tego samego profilu korzystają ze wspólnego salda.
- Jest to **lokalne oszacowanie**, nie odczyt rozliczeń ani metryk Azure. Nie wymaga
  Microsoft Entra. Użycie poza kontrolowanym procesem wymaga ręcznej korekty.
  Zmiana klucza lub regionu oznacza inny profil; należy ustawić jego właściwe saldo.
  Inni dostawcy mogą stosować przeliczniki znaków na kredyty zależne od modelu.
- Azure liczy przygotowany tekst jako punkty kodowe Unicode (z wagą 2 dla znaków
  ideograficznych CJK), ze spacjami i interpunkcją. Obecny generator otacza zwykły
  tekst znacznikami `speak`/`voice`; sposób syntezy i format odpowiedzi nie zostały
  zmienione. Wartość nie jest liczbą bajtów UTF-8 ani długością pliku SRT.
- Przed wysłaniem żądania rezerwowane są znaki. Przetworzona odpowiedź Azure
  zatwierdza zużycie także wtedy, gdy późniejszy zapis lub konwersja audio zawiedzie.
  Jednoznaczne odrzucenie żądania zwalnia rezerwację. Zerwane połączenie, przerwanie
  w trakcie wysyłania lub niejednoznaczny błąd zachowuje rezerwację; program nie
  zakłada wtedy, że ponowienie będzie bezpłatne. Wpisanie skorygowanego dostępnego
  salda rozlicza dotychczasowe rezerwacje.
- Reset odbywa się według lokalnej daty komputera, raz na okres, również po długiej
  przerwie w uruchamianiu programu. Dzień 29–31 w krótszym miesiącu oznacza ostatni
  dzień tego miesiąca. Zmiana samego limitu lub dnia nie przywraca zużytego salda.
- HTTP 429 sam w sobie nie potwierdza wyczerpania limitu miesięcznego. Azure ma
  maksymalnie pięć prób z przerwami i obsługą `Retry-After`. Jawny błąd limitu
  znaków zatrzymuje pracę od razu i blokuje dalsze wysyłanie dla tego profilu
  do resetu lub ręcznej korekty dostępnego salda. Oczekiwanie można przerwać przyciskiem Anuluj.
  Niepotwierdzone błędy nie uruchamiają automatycznej ponownej płatnej syntezy.

### Odzyskiwanie i wznowienie

Gotowe surowe fragmenty i ich sumy kontrolne są przechowywane w `TtsJobs`, poza
usuwanym katalogiem tymczasowym. Zapis postępu jest powiązany z tekstem, czasami
oraz ustawieniami głosu i silnika. Ponowne uruchomienie dla tego samego materiału
pozwala wykorzystać zachowane fragmenty bez kolejnego TTS i bez ponownego obciążenia
licznika. Można też jawnie wybrać ponowne wygenerowanie całości.

Po zatrzymaniu wskutek limitu, błędu lub anulowania użytkownik może:

- zapisać UTF-8 SRT z nieukończonymi liniami, włącznie z wysłaną linią bez poprawnego
  audio; zachowane są oryginalne czasy, numeracja rozpoczyna się od 1;
- połączyć gotowe fragmenty dotychczasowym procesem korekty i scalania w częściowy
  WAV, z zachowaniem pozycji czasowych i ciszy w brakujących miejscach. Nie jest
  uruchamiana ponowna synteza ani tworzenie MKV z niepełnego lektora.

Po potwierdzeniu zapisu wybiera się ścieżki SRT i WAV. Postęp pozostaje zachowany
również po odmowie zapisu lub zamknięciu programu. Po zakończeniu projektu można
ręcznie usunąć odpowiedni katalog w `TtsJobs`, aby zwolnić miejsce; usunięcie cache
nie przywraca zużytych znaków.

W wydaniu 4.2.3 moduły miksowania i muksowania MKV pozostały takie jak w 4.2.2.
Wydanie 4.2.3 nie zawierało funkcji masowych 5.1 i 5.2; są one dostępne od 5.0,
opisanej na początku tej instrukcji.

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
4. Po zapisaniu WAV otwiera się okno **Lektor PL — wersja 4.2.3**. Wybierz konkretną
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
