# Home — approved clean mockup

Status: **approved clean baseline**.

## Design direction
- Netflix/Plex-inspired media-first Home.
- Clean design is the UX/layout baseline.
- The elaborate Japanese Jularr look is a later theme/skin over the same structure.
- **Light and Dark are both first-class and must both be polished on every major screen.**
- Desktop, Tablet, Mobile and TV use the same information architecture with platform-specific navigation and interaction.

## Home content
Priority:
1. Hero / featured media
2. Continue Watching / Reading / Listening
3. Up Next
4. For You
5. Trending / discovery rows
6. New episodes from the user's library
7. Upcoming releases
8. dynamic genre/media rows

Do not turn Home into an admin/dashboard surface. No library-count cards, storage widgets, download/import widgets or dense technical metadata.

## Navigation
### Desktop / tablet wide
- Home
- Library
- Calendar
- Learning
- global search at top
- Profile / Settings at bottom
- Admin is a separate authorized mode

### Mobile
- Home
- Library
- Calendar
- Learning
- Profile
- global search at top

### TV
Leanback/focus-first version of the same core destinations.

## Hero / banner system
The Hero must work even when a provider does not supply a perfect banner.

### Artwork source priority
1. real provider backdrop/banner if suitable
2. poster/cover + generated blurred/gradient background
3. clean cover-first fallback with calm derived background

AI-generated artwork is never required for a valid Hero.

### Hero item priority
1. Continue/resume
2. Up Next
3. personalized For You recommendation
4. newly relevant local-library item
5. Trending/discovery fallback

### Hero content
- title
- compact media metadata
- short description
- progress when relevant
- primary CTA: Continue / Play / Read / Listen
- minimal secondary actions

## Reference image
See `home-clean-approved.jpg`. The image is a visual direction reference; this document is authoritative for behavior/content decisions.
