# 3D Spatial Tree Performance Benchmarks

## TL;DR: What Problem This Solves

- Need fast “what’s near X?” or “what’s inside this volume?” in 3D.
- These structures avoid scanning every object; queries touch only nearby data.
- Quick picks: OctTree3D for general 3D queries; KdTree3D for nearest‑neighbor on points; RTree3D for volumetric bounds.

Note: KdTree3D, OctTree3D, and RTree3D are under active development and their APIs/performance may evolve. SpatialHash3D is stable and recommended for broad‑phase neighbor queries with many moving objects.

For boundary and result semantics across structures, see [Spatial Tree Semantics](../features/spatial/spatial-tree-semantics.md)

This document contains performance benchmarks for the 3D spatial tree implementations in Unity Helpers.

## Available 3D Spatial Trees

- **OctTree3D** - Easiest to use, good all-around performance for 3D
- **KdTree3D** - Balanced and unbalanced variants available
- **RTree3D** - Optimized for 3D bounding box queries
- **SpatialHash3D** - Efficient for uniformly distributed moving objects (stable)

## Performance Benchmarks

<!-- SPATIAL_TREE_3D_BENCHMARKS_START -->

### Datasets

<!-- tabs:start -->

#### **1,000,000 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">1,000,000 entries</td><td align="right">3 (0.258s)</td><td align="right">6 (0.154s)</td><td align="right">2 (0.386s)</td><td align="right">3 (0.297s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=49.50)</td><td align="right">20</td><td align="right">23</td><td align="right">29</td><td align="right">14</td></tr>
    <tr><td align="left">Half (~span/4) (r=24.75)</td><td align="right">151</td><td align="right">166</td><td align="right">164</td><td align="right">116</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=12.38)</td><td align="right">1,024</td><td align="right">1,260</td><td align="right">1,550</td><td align="right">1,286</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">23,627</td><td align="right">22,558</td><td align="right">130,696</td><td align="right">72,437</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size≈99.00x99.00x99.00)</td><td align="right">30</td><td align="right">35</td><td align="right">172</td><td align="right">20</td></tr>
    <tr><td align="left">Half (size≈49.50x49.50x49.50)</td><td align="right">40</td><td align="right">48</td><td align="right">1,270</td><td align="right">218</td></tr>
    <tr><td align="left">Quarter (size≈24.75x24.75x24.75)</td><td align="right">40</td><td align="right">51</td><td align="right">3,601</td><td align="right">2,338</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">42</td><td align="right">53</td><td align="right">161,979</td><td align="right">70,789</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">500 neighbors</td><td align="right">6,071</td><td align="right">10,425</td><td align="right">2,251</td><td align="right">301</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">63,996</td><td align="right">71,939</td><td align="right">10,430</td><td align="right">3,189</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">295,220</td><td align="right">302,020</td><td align="right">15,148</td><td align="right">6,980</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">326,786</td><td align="right">340,479</td><td align="right">18,325</td><td align="right">7,588</td></tr>
  </tbody>
</table>

#### **100,000 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100,000 entries</td><td align="right">49 (0.020s)</td><td align="right">97 (0.010s)</td><td align="right">63 (0.016s)</td><td align="right">8 (0.120s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=49.50)</td><td align="right">381</td><td align="right">592</td><td align="right">769</td><td align="right">176</td></tr>
    <tr><td align="left">Half (~span/4) (r=24.75)</td><td align="right">1,135</td><td align="right">1,650</td><td align="right">1,860</td><td align="right">727</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=12.38)</td><td align="right">2,814</td><td align="right">4,499</td><td align="right">5,946</td><td align="right">3,021</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">26,910</td><td align="right">30,212</td><td align="right">163,640</td><td align="right">93,042</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size≈99.00x99.00x9)</td><td align="right">597</td><td align="right">688</td><td align="right">3,044</td><td align="right">352</td></tr>
    <tr><td align="left">Half (size≈49.50x49.50x4.5)</td><td align="right">674</td><td align="right">850</td><td align="right">8,858</td><td align="right">3,459</td></tr>
    <tr><td align="left">Quarter (size≈24.75x24.75x2.25)</td><td align="right">710</td><td align="right">870</td><td align="right">42,334</td><td align="right">23,513</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">728</td><td align="right">880</td><td align="right">208,362</td><td align="right">92,740</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">500 neighbors</td><td align="right">6,826</td><td align="right">12,399</td><td align="right">1,582</td><td align="right">266</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">39,102</td><td align="right">44,932</td><td align="right">8,701</td><td align="right">2,065</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">320,222</td><td align="right">258,261</td><td align="right">18,061</td><td align="right">6,748</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">329,497</td><td align="right">259,583</td><td align="right">27,859</td><td align="right">10,618</td></tr>
  </tbody>
</table>

#### **10,000 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">10,000 entries</td><td align="right">232 (0.004s)</td><td align="right">776 (0.001s)</td><td align="right">599 (0.002s)</td><td align="right">435 (0.002s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=49.50)</td><td align="right">5,010</td><td align="right">5,079</td><td align="right">9,139</td><td align="right">1,721</td></tr>
    <tr><td align="left">Half (~span/4) (r=24.75)</td><td align="right">6,250</td><td align="right">7,021</td><td align="right">8,493</td><td align="right">3,453</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=12.38)</td><td align="right">6,336</td><td align="right">7,326</td><td align="right">10,677</td><td align="right">6,600</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">42,213</td><td align="right">38,535</td><td align="right">199,646</td><td align="right">142,424</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size≈99.00x9x9)</td><td align="right">6,158</td><td align="right">6,040</td><td align="right">31,575</td><td align="right">3,564</td></tr>
    <tr><td align="left">Half (size≈49.50x4.5x4.5)</td><td align="right">6,862</td><td align="right">6,784</td><td align="right">41,741</td><td align="right">36,487</td></tr>
    <tr><td align="left">Quarter (size≈24.75x2.25x2.25)</td><td align="right">7,035</td><td align="right">7,114</td><td align="right">147,595</td><td align="right">111,575</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">6,857</td><td align="right">7,135</td><td align="right">273,980</td><td align="right">147,480</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">500 neighbors</td><td align="right">9,990</td><td align="right">10,905</td><td align="right">633</td><td align="right">181</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">49,033</td><td align="right">67,725</td><td align="right">5,928</td><td align="right">2,087</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">222,009</td><td align="right">313,530</td><td align="right">25,978</td><td align="right">11,079</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">352,912</td><td align="right">395,316</td><td align="right">42,060</td><td align="right">18,631</td></tr>
  </tbody>
</table>

#### **1,000 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">1,000 entries</td><td align="right">5,047 (0.000s)</td><td align="right">7,017 (0.000s)</td><td align="right">4,221 (0.000s)</td><td align="right">4,056 (0.000s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=4.5)</td><td align="right">13,309</td><td align="right">15,407</td><td align="right">24,555</td><td align="right">19,376</td></tr>
    <tr><td align="left">Half (~span/4) (r=2.25)</td><td align="right">54,631</td><td align="right">64,832</td><td align="right">119,069</td><td align="right">122,189</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=1.13)</td><td align="right">63,809</td><td align="right">66,230</td><td align="right">302,733</td><td align="right">190,679</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">63,925</td><td align="right">66,775</td><td align="right">297,025</td><td align="right">189,076</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size≈9x9x9)</td><td align="right">56,025</td><td align="right">60,364</td><td align="right">285,851</td><td align="right">34,830</td></tr>
    <tr><td align="left">Half (size≈4.5x4.5x4.5)</td><td align="right">60,435</td><td align="right">65,721</td><td align="right">167,043</td><td align="right">160,053</td></tr>
    <tr><td align="left">Quarter (size≈2.25x2.25x2.25)</td><td align="right">60,892</td><td align="right">67,241</td><td align="right">408,947</td><td align="right">203,923</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">60,859</td><td align="right">66,633</td><td align="right">408,812</td><td align="right">205,130</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">500 neighbors</td><td align="right">15,079</td><td align="right">15,221</td><td align="right">3,229</td><td align="right">617</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">66,517</td><td align="right">65,354</td><td align="right">15,372</td><td align="right">3,961</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">326,664</td><td align="right">303,344</td><td align="right">69,259</td><td align="right">27,619</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">372,951</td><td align="right">425,921</td><td align="right">78,021</td><td align="right">35,781</td></tr>
  </tbody>
</table>

#### **100 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100 entries</td><td align="right">38,461 (0.000s)</td><td align="right">36,496 (0.000s)</td><td align="right">26,178 (0.000s)</td><td align="right">18,552 (0.000s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=4.5)</td><td align="right">128,838</td><td align="right">126,647</td><td align="right">268,664</td><td align="right">156,643</td></tr>
    <tr><td align="left">Half (~span/4) (r=2.25)</td><td align="right">132,912</td><td align="right">157,069</td><td align="right">283,144</td><td align="right">241,941</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=1.13)</td><td align="right">155,589</td><td align="right">157,614</td><td align="right">341,201</td><td align="right">328,411</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">156,184</td><td align="right">155,346</td><td align="right">349,424</td><td align="right">328,728</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size≈9x4x1)</td><td align="right">444,834</td><td align="right">441,395</td><td align="right">1,132,436</td><td align="right">267,317</td></tr>
    <tr><td align="left">Half (size≈4.5x2x1)</td><td align="right">458,537</td><td align="right">464,441</td><td align="right">413,889</td><td align="right">347,753</td></tr>
    <tr><td align="left">Quarter (size≈2.25x1x1)</td><td align="right">468,261</td><td align="right">464,503</td><td align="right">578,248</td><td align="right">476,261</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">469,295</td><td align="right">459,734</td><td align="right">573,732</td><td align="right">477,462</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree3D (Balanced)</th>
      <th align="right">KDTree3D (Unbalanced)</th>
      <th align="right">OctTree3D</th>
      <th align="right">RTree3D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100 neighbors (max)</td><td align="right">87,622</td><td align="right">81,758</td><td align="right">60,944</td><td align="right">57,383</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">381,364</td><td align="right">355,350</td><td align="right">93,401</td><td align="right">74,007</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">439,990</td><td align="right">402,016</td><td align="right">146,308</td><td align="right">149,981</td></tr>
  </tbody>
</table>
<!-- tabs:end -->
<!-- SPATIAL_TREE_3D_BENCHMARKS_END -->

## Interpreting the Results

All numbers represent **operations per second** (higher is better), except for construction times which show operations per second and absolute time.

### Choosing the Right Tree

**OctTree3D**:

- Best for: General-purpose 3D spatial queries
- Strengths: Balanced performance, easy to use, good spatial locality
- Use cases: 3D collision detection, visibility culling, spatial audio

**KdTree3D (Balanced)**:

- Best for: Nearest-neighbor queries in 3D space
- Strengths: Fast point queries, good for smaller datasets
- Use cases: Pathfinding, AI spatial awareness, particle systems

**KdTree3D (Unbalanced)**:

- Best for: When you need fast construction and will rebuild frequently
- Strengths: Fastest construction, similar query performance to balanced
- Use cases: Dynamic environments, frequently changing spatial data

**RTree3D**:

- Best for: 3D bounding box queries, especially with volumetric data
- Strengths: Excellent for large bounding volumes, handles overlapping objects
- Use cases: Physics engines, frustum culling, volumetric effects

### Important Notes

- All spatial trees assume **immutable** positional data
- If positions change, you must reconstruct the tree
- Spatial queries are O(log n) vs O(n) for linear search
- 3D trees have higher construction costs than 2D variants due to additional dimension
- Construction cost is amortized over many queries
