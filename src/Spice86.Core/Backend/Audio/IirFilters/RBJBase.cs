/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 *  Copyright (c) 2009 by Vinnie Falco
 *  Copyright (c) 2016 by Bernd Porr
 */
namespace Spice86.Core.Backend.Audio.IirFilters;

/**
 * Filter realizations based on Robert Bristol-Johnson formulae:
 *
 * http://www.musicdsp.org/files/Audio-EQ-Cookbook.txt
 *
 * These are all 2nd order filters which are tuned with the Q (or Quality factor).
 * The Q factor causes a resonance at the cutoff frequency. The higher the Q
 * factor the higher the responance. If 0.5 < Q < 1/sqrt(2) then there is no resonance peak.
 * Above 1/sqrt(2) the peak becomes more and more pronounced. For bandpass and stopband
 * the Q factor is replaced by the width of the filter. The higher Q the more narrow
 * the bandwidth of the notch or bandpass.
 *
 **/

internal class RbjBase : Biquad {
    private readonly DirectFormI _state = new();

    public double Filter(double s) {
        return _state.Process1(s, this);
    }

    public DirectFormI GetState() {
        return _state;
    }
}