# Running a VPB rendezvous

This is for someone who has decided to run one. If you are a player trying to connect to a friend, you do not need this file — you need an address from whoever is running one, or you need to read [Do you actually need this?](#do-you-actually-need-this) and conclude that you do not.

*Nothing here is legal advice. It is an engineering description of what the software does and does not do, plus the practical risks an operator actually runs into. Jurisdictions differ. If you are worried, talk to a lawyer rather than to a README.*

---

## What it is

Two people behind home routers cannot address each other directly. Neither knows what its own router rewrote its source port to, so neither can tell the other where to send. A rendezvous solves exactly that: both sides send it one datagram, it reports back the address it saw for each of them, and they then send to each other. The packets they exchange while doing so punch the holes their routers need.

That is the whole job. It runs for a few seconds at the start of a session and then has nothing more to do.

**The relay is a separate thing that shares the same binary.** Some pairs of routers cannot be punched through at all — most commonly when both sides are behind carrier-grade NAT, where the mapping changes per destination and the address the rendezvous saw is already wrong by the time it is used. For those pairs, and only those, VPB falls back to sending session traffic *through* the relay. That costs real bandwidth, which is why you can turn it off and still be useful.

---

## Do you actually need this?

Most people should answer no.

| Situation | What to use |
|---|---|
| Both on the same LAN | Nothing. Use the LAN address directly. |
| One side can forward a UDP port | Nothing. That side hosts, hands out an invite code, done. |
| You already have an address from someone | Use theirs. |
| Neither side can forward a port and neither has an address to use | A rendezvous |

VPB ships with **no default rendezvous address, no built-in list, and no recommended endpoint**, and it never will. That is deliberate. A curated list makes this project the operator of a network, which is the position it exists to stay out of. You use one you were given, or you run your own for yourself and the people you actually play with.

---

## Running it

```bash
VpbNet.exe --rendezvous 47773
```

That is the whole thing. It binds one UDP port, dual-stack, and holds no files.

| Flag | Effect |
|---|---|
| `--rendezvous <port>` | Run as a rendezvous on that UDP port. Default 47773. |
| `--no-relay` | Answer rendezvous requests, but never forward session traffic. See [Sizing](#sizing). |
| `--verbose` | Print counters every 30 s. Counts only — see [What it can see](#what-it-can-see). |

Requirements are small enough to be uninteresting: any always-on machine with a routable address, one open UDP port inbound, and a few megabytes of memory. It is single-threaded, does no disk I/O, and idles at effectively zero CPU. A home PC with a port forward works; so does the smallest VPS anyone sells.

There is no config file, no database, and no install step. Stop it with Ctrl-C; there is nothing to clean up.

---

## What it can see

This is the part worth being precise about, because "trust me" is not a security model and the people using your rendezvous deserve better than a promise.

| | Can the operator see it? |
|---|---|
| That two addresses wanted to meet | **Yes**, for the ~60 s their entries live |
| The IP addresses of both peers | **Yes** — it is talking to them |
| The room code | No. Peers send a token derived from it through one slow PBKDF2 pass; the code is not recoverable from the token without grinding it. |
| The session key | No, and it is not derivable from the token the rendezvous holds. Session datagrams are authenticated with a key the rendezvous never sees. |
| Anything inside a relayed datagram | No. It forwards bytes it cannot read and could not forge. |
| Nicknames, scene names, package lists, chat | No. None of it is ever sent to a rendezvous, in any mode. |
| Who is playing with whom, historically | No. Nothing is written to disk, ever. |

`--verbose` prints room count, peer count, and how many datagrams were served, relayed, refused and ignored. **There is no flag that logs a token, an address, or a pairing, and adding one would defeat the point of the design.** If you find yourself wanting one to debug something, the counters plus a packet capture on your own machine will tell you what you need without turning your box into a record of who met whom.

### What it holds, and for how long

- An endpoint lives **60 seconds** past its last announce, then it is gone. Peers re-announce every 500 ms while a session is being set up, so live sessions stay; abandoned ones evaporate.
- Empty rooms are removed entirely.
- Everything is in memory. There is no file, no database, and no way to configure one.
- Rooms, peers per room, and rate-limit sources are all capped, so nothing grows without bound on traffic a stranger sends you.

A takedown notice has nothing to take down and a data request has nothing to hand over — not because of a policy, but because there is nothing there.

---

## Sizing

A rendezvous is free. A relay is not. Know which you are signing up for.

**Rendezvous traffic** is one 128-byte request and one ~40-byte reply per peer every 500 ms, only while a session is being established. That is about **2.7 kbit/s per peer**, for a few seconds. You could run this on a phone.

**Relay traffic** carries the whole session. A pose frame is 200 bytes at 45 Hz — 72 kbit/s each way — plus 28 bytes of relay header per datagram, which brings it to about 82 kbit/s. The relay both receives and re-sends every datagram:

- **~0.16 Mbit/s of egress per relayed session**, sustained for as long as people are playing
- **~74 MB of egress per session-hour**
- A **1 TB/month** allowance is therefore about **13,500 session-hours**, or roughly **18 sessions running continuously**, or far more in practice since nobody plays 24/7

Size on monthly transfer, not on bandwidth rate. The rate is trivial and will mislead you; the transfer allowance is what a cheap VPS actually bills for.

If you want to help people find each other without carrying their traffic, run with `--no-relay`. Peers who cannot punch through will time out and be told to find a relay, which is an honest outcome and costs you nothing.

---

## The risks, in the order they actually matter

**1. Your VPS provider's terms of service.** This is the real one and it is mundane. Many hosts prohibit adult-adjacent services outright, some prohibit anything that looks like a proxy or relay, and enforcement is usually a suspension email rather than anything dramatic. **Read the AUP before you sign up, not after.** This is by far the most likely way running one of these ends.

**2. Bandwidth billing.** See [Sizing](#sizing). Overage charges on cheap VPS plans can be brutal and are easy to trigger with a relay you forgot was on. `--no-relay` exists for this.

**3. Abuse-desk complaints about your IP.** A UDP service that forwards traffic will occasionally attract a complaint, usually automated. Being able to say "it forwards opaque bytes for a fixed 60-second window, keeps no logs, and here is the source" is a much better position than not being able to.

**4. Everything else.** The relay is a conduit: it does not store content, does not select recipients beyond "the other end of a circuit that both parties joined", and does not modify what passes through. That is the shape that intermediary/"mere conduit" regimes are written around, and it is why the design refuses to grow features that would change it. It is not a guarantee, and it is not advice — it is why the software is built the way it is.

### Risks people worry about that are not the issue

- **Being able to read what people are doing.** You cannot, and this is enforced by the protocol rather than by policy.
- **Amplification attacks.** Requests are zero-padded to 128 bytes so a reply is always smaller than what triggered it. The service cannot be used to attack a third party by volume.
- **Being used as a reflector.** Forwarding requires a ticket that only ever travelled to the address it was issued to. Someone spoofing a victim's source address never receives one, so they cannot aim your relay at anybody.
- **Someone joining sessions they were not invited to.** They cannot. Reaching your rendezvous, seeing a token, or even learning both endpoints gains an outsider nothing — session datagrams are authenticated with a key derived from the room code, which the rendezvous never sees.

---

## Running it well

- **Give the address only to people you mean to give it to.** There is no access control, by design; obscurity is the only gate and it is a weak one. It does not need to be strong, because seeing your rendezvous gains an attacker nothing.
- **Do not publish it in a public list.** Not because it would break, but because a directory of relays is a network, and operating a network is a different thing from running a program for your friends.
- **Do not add logging.** If you fork this and add it, say so loudly to the people using it.
- **Do not charge for it.** Taking money turns "a program I run" into "a service I sell", which changes your obligations in essentially every jurisdiction and is not worth it for something that costs you a few gigabytes a month.
- **Keep the binary updated** if the protocol version moves. Peers on a newer version are refused by name and told to update, so a stale rendezvous is a visible failure rather than a silent one.

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| Peers report "no answer from the rendezvous" | Port not open inbound, wrong address given out, or the process is not running. Check with a packet capture before assuming the software. |
| "the rendezvous is answering but has not seen the other peer" | Both peers must use the **same room code** *and* the **same rendezvous address**. Usually one of them typed a different code, or has not started yet. |
| Peers connect but immediately fall back to the relay | Normal for some router pairs. If it happens for everyone, check that your box is not rewriting the source ports it reports — the reported address must be what the outside world sees. |
| `--verbose` shows `ignored` climbing | Unparseable or unauthorised datagrams. Internet background noise on an open UDP port; only interesting if it correlates with real failures. |
| Relay counter stays at 0 | Either nobody has needed it, or you are running `--no-relay`. |

---

## For the curious

- Wire format: `protocol/VpbNetRendezvous.cs`
- What the server decides, with no sockets in it: `tools/VpbNet/Rendezvous/RendezvousTable.cs`
- The socket loop: `tools/VpbNet/Rendezvous/RendezvousServer.cs`
- Tests, including the reflector and spoofing cases: `VpbNet.exe --self-test-rendezvous`

Every claim on this page is asserted by a test in that last one. If you change the code, run it.
