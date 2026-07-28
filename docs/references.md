# References

Consolidated bibliography, IEEE-numbered, grouped by topic. Numbers are stable once assigned — add new entries at the end of the relevant group with the next unused number. Technical-reference pages cite these numbers.

## Risk analysis methodology

[1] U.S. Army Corps of Engineers, *Safety of Dams — Policy and Procedures*, ER 1110-2-1156, Washington, DC, 2014.

[2] U.S. Army Corps of Engineers and U.S. Bureau of Reclamation, *Best Practices in Dam and Levee Safety Risk Analysis*, 2019.

## RMC-TotalRisk software

[3] C. H. Smith, "A New Suite of Risk Analysis Software for Dam and Levee Safety," *Journal of Dam Safety*, vol. 18, no. 3, 2021.

[4] C. H. Smith, "A New Comprehensive Risk Analysis Software, RMC-TotalRisk," in *Proc. ANCOLD Conference*, 2022.

[5] C. H. Smith, "A New Quantitative Risk Analysis Software for Dam and Levee Safety, RMC-TotalRisk," in *Proc. ASDSO Dam Safety Conference*, 2024.

[6] U.S. Army Corps of Engineers, Risk Management Center, *RMC-TotalRisk User's Guide*, v1.0.

[7] U.S. Army Corps of Engineers, Risk Management Center, *RMC-TotalRisk Technical Reference Manual* (draft).

[8] U.S. Army Corps of Engineers, Risk Management Center, *RMC-TotalRisk Verification Report* (draft).

## Depth-damage and consequence functions

[9] G. Papathoma-Köhle et al., "Flood Depth-Damage Functions for Built Environment," *Environmental Processes*, vol. 1, pp. 553–572, 2014. doi:10.1007/s40710-014-0038-2. (Power function y = a·x^b is the best fit to post-flood field-survey damage ratios; R² 0.68–0.78 across occupancy classes.)

[10] Effects of damage initiation points of depth-damage functions on flood risk assessment, *npj Natural Hazards*, vol. 1, art. 4, 2024. doi:10.1038/s44304-024-00004-z.

[11] O. E. J. Wing et al., "New insights into US flood vulnerability revealed from flood insurance big data," *Nature Communications*, vol. 11, art. 1444, 2020. doi:10.1038/s41467-020-15264-2. (Observed NFIP damage ratios at a given depth are beta-distributed and often bimodal.)

[12] A duration-dependent flood depth-damage function calibrated using FEMA NFIP claims, *Nature-Based Solutions / ScienceDirect*, 2026. (Generalized logistic (Richards) bounded S-curves on NFIP claims.)

[13] J. Huizinga, H. de Moel, and W. Szewczyk, *Global Flood Depth-Damage Functions: Methodology and the Database with Guidelines*, JRC Technical Report JRC105688, European Commission, 2017. (Continental damage-fraction curves bounded at maximum damage.)

[14] U.S. Army Corps of Engineers, *Generic Depth-Damage Relationships*, Economic Guidance Memorandum 01-03 (and EGM 04-01), Washington, DC, 2000/2003; and Hydrologic Engineering Center, *HEC-FDA Technical Reference* (tabular depth-percent damage with per-ordinate error distributions).

## Numerical integration and tail risk measures

[15] R. Piessens, E. de Doncker-Kapenga, C. W. Überhuber, and D. K. Kahaner, *QUADPACK: A Subroutine Package for Automatic Integration*, Springer-Verlag, 1983. (Gauss–Kronrod adaptive quadrature; the G10K21 rule the engine's 1D integrator uses.)

[16] G. P. Lepage, "A New Algorithm for Adaptive Multidimensional Integration," *Journal of Computational Physics*, vol. 27, no. 2, pp. 192–203, 1978. doi:10.1016/0021-9991(78)90004-9. (The VEGAS importance-sampling algorithm used for joint multi-component risk.)

[17] R. T. Rockafellar and S. Uryasev, "Optimization of Conditional Value-at-Risk," *Journal of Risk*, vol. 2, no. 3, pp. 21–41, 2000. (CVaR / expected shortfall as a coherent tail risk measure.)

[18] Basel Committee on Banking Supervision, *Minimum Capital Requirements for Market Risk* (Fundamental Review of the Trading Book), Bank for International Settlements, 2019. (Adoption of 97.5% expected shortfall in place of 99% VaR — the regulatory precedent for preferring a coherent tail measure.)

## Event-tree and fault-tree analysis

[19] U.S. Nuclear Regulatory Commission, *Fault Tree Handbook*, NUREG-0492, Washington, DC, 1981. Available: https://www.nrc.gov/reading-rm/doc-collections/nuregs/staff/sr0492/index

[20] National Aeronautics and Space Administration, *Fault Tree Handbook with Aerospace Applications*, Version 1.1, Washington, DC, 2002. Available: https://extapps.ksc.nasa.gov/Reliability/Documents/Fault_Tree_Handbook_with_Aerospace_Applications_August_2002.pdf

[21] International Electrotechnical Commission, *Fault Tree Analysis (FTA)*, IEC 61025:2006, Geneva, Switzerland, 2006. Available: https://webstore.iec.ch/en/publication/4311

[22] International Electrotechnical Commission, *Analysis Techniques for Dependability — Event Tree Analysis (ETA)*, IEC 62502:2010, Geneva, Switzerland, 2010.
